using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig; 
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

namespace BriefGenerator.Api.Services
{
    public class AiProcessingService : IAiProcessingService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AiProcessingService> _logger;
        private readonly string _apiKey;

        public AiProcessingService(AppDbContext context, IConfiguration configuration, ILogger<AiProcessingService> logger)
        {
            _context = context;
            _logger = logger;
            
            _apiKey = configuration["GeminiApiKey"] ?? configuration["OpenAIApiKey"] ?? throw new InvalidOperationException("Gemini API key missing");
        }

        public async Task ProcessIntakeAsync(Guid intakeId)
        {
            var intakeSession = await _context.IntakeSessions.FindAsync(intakeId);
            if (intakeSession == null)
            {
                _logger.LogWarning("Intake session {IntakeId} not found.", intakeId);
                return;
            }

            intakeSession.Status = "processing";
            await _context.SaveChangesAsync();

            try
            {
                var files = await _context.UploadedFiles.Where(f => f.IntakeSessionId == intakeId).ToListAsync();
                string combinedExtractedText = string.Empty;

                // Task C1: Extract Text
                foreach (var file in files)
                {
                    var text = await ExtractTextFromFileAsync(file);
                    file.ExtractedText = text;
                    combinedExtractedText += $"--- Source: {file.FileName} ---\n{text}\n\n";
                }
                await _context.SaveChangesAsync();

                // Task C2: Generate Structured Brief
                var structuredBrief = await GenerateStructuredBriefAsync(combinedExtractedText);

                // Task C3: Detect Missing Information
                var missingInfo = await DetectMissingInformationAsync(structuredBrief);

                var brief = new Brief
                {
                    Id = Guid.NewGuid(),
                    IntakeSessionId = intakeId,
                    StructuredJson = structuredBrief,
                    MissingFieldsJson = missingInfo,
                    MarkdownOutput = "Markdown representation goes here...", 
                    Status = "awaiting_client"
                };

                // After a brief is generated (and a share link is created), the next expected state is client review.
                intakeSession.Status = "awaiting_client";
                _context.Briefs.Add(brief);

                // FLOW A FIX: Automatically generate the Client Share Token!
                var shareLink = new ShareLink
                {
                    Id = Guid.NewGuid(),
                    BriefId = brief.Id,
                    Token = Guid.NewGuid().ToString("N"),
                    ExpiresAt = DateTime.UtcNow.AddDays(7)
                };
                _context.ShareLinks.Add(shareLink);

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow failed for IntakeSession {IntakeId}", intakeId);
                intakeSession.Status = "failed";
                await _context.SaveChangesAsync();
            }
        }

        public async Task<string> ExtractTextFromFileAsync(UploadedFile file)
        {
            if (string.IsNullOrWhiteSpace(file.StoragePath) || !File.Exists(file.StoragePath))
            {
                _logger.LogWarning("File not found at path: {Path}", file.StoragePath);
                return string.Empty;
            }

            var extension = Path.GetExtension(file.StoragePath).ToLowerInvariant();

            try
            {
                // 1. Plain Text
                if (extension is ".txt" or ".md" or ".json" or ".csv")
                {
                    return await File.ReadAllTextAsync(file.StoragePath);
                }

                // 2. Audio/Video (Gemini Native Audio/Video)
                if (extension is ".mp3" or ".wav" or ".m4a" or ".ogg" or ".mp4")
                {
                    _logger.LogInformation("Transcribing media file: {FileName}", file.FileName);
                    var audioBytes = await File.ReadAllBytesAsync(file.StoragePath);
                    var mimeType = extension switch { ".mp3" => "audio/mp3", ".wav" => "audio/wav", ".m4a" => "audio/mp4", ".ogg" => "audio/ogg", ".mp4" => "video/mp4", _ => "audio/mp3" };
                    return await CallGeminiAsync("You are a helpful assistant.", "Transcribe the following media exactly:", mimeType, audioBytes);
                }

                // 3. Images (OCR via Gemini Vision)
                if (extension is ".png" or ".jpg" or ".jpeg" or ".webp")
                {
                    _logger.LogInformation("Extracting text from image: {FileName}", file.FileName);
                    
                    var imageBytes = await File.ReadAllBytesAsync(file.StoragePath);
                    var mimeType = extension == ".png" ? "image/png" : 
                                   extension == ".webp" ? "image/webp" : "image/jpeg";

                    return await CallGeminiAsync("You are an OCR and image analysis assistant.", "Extract all readable text from this image. If it's a diagram or sketch, describe the contents and flow in detail.", mimeType, imageBytes);
                }

                // 4. PDFs (PdfPig Text Extraction)
                if (extension is ".pdf")
                {
                    _logger.LogInformation("Extracting text from PDF: {FileName}", file.FileName);
                    
                    return await Task.Run(() => 
                    {
                        var textBuilder = new StringBuilder();
                        using (var document = PdfDocument.Open(file.StoragePath))
                        {
                            foreach (var page in document.GetPages())
                            {
                                textBuilder.AppendLine(page.Text);
                            }
                        }
                        return textBuilder.ToString();
                    });
                }

                _logger.LogWarning("Unsupported file type for extraction: {Extension}", extension);
                return $"[Unsupported file format: {file.FileName}]";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from {FileName}", file.FileName);
                return string.Empty;
            }
        }

        public async Task<string> GenerateStructuredBriefAsync(string extractedText)
        {
            _logger.LogInformation("Generating structured brief using LLM.");

            var systemPrompt = @"You are a requirements clarification specialist. Your role is to transform raw client input—whether it's free text, a voice transcription, a screenshot interpretation, or any combination—into a structured, actionable project brief that surfaces ambiguities and validates understanding before work begins.

**Your Task**
Take whatever client input you receive and produce a clean, professional brief that:
1. Translates what the client said into plain-language goals and success criteria
2. Identifies gaps, unclear points, or contradictions in their request
3. Suggests 3-5 clarifying questions to send back to the client
4. Formats everything in a readable, scannable structure ready to share

**How to Handle Input**
Process all input as legitimate client communication. If multiple formats are provided, synthesize them into one coherent brief—don't treat them as separate items.

**What to Extract**
From the client input, identify:
- What they actually want and explicit goals (what success looks like)
- Success criteria (how they'll measure if the work is done right)
- Scope (what's included, what isn't)
- Timeline or urgency
- Audience or stakeholders
- Any constraints or requirements (budget, platform, format, brand guidelines, etc.)

**What to Flag as Unclear & Suggested Questions**
Identify vague terms, missing information, contradictions, or scope creep. Generate 3-5 targeted, conversational follow-up questions to lock down scope and prevent rework.

**Tone & Style**
- Write in professional but approachable language
- Be specific and concrete—no vague adjectives
- Flag assumptions you made to fill gaps
- Prioritize clarity over completeness

**Output Format**
You MUST output EXACTLY this JSON structure. Map your findings to these specific fields.
{
  ""project_name"": ""[Concise summary of what they want in 1-2 sentences]"",
  ""client_name"": """",
  ""business_goal"": ""[Combine the Goals & Success Criteria here]"",
  ""target_users"": [""[Audience 1]"", ""[Audience 2]""],
  ""features"": [""[Scope item 1]"", ""[Scope item 2]""],
  ""platforms"": [],
  ""design_requirements"": [],
  ""technical_constraints"": [],
  ""timeline"": """",
  ""budget"": """",
  ""missing_information"": []
}
If any piece of information is missing from the raw text, leave the string empty or the array empty.";

            try
            {
                var responseText = await CallGeminiAsync(systemPrompt, $"Raw Text:\n{extractedText}");
                return responseText.Replace("```json", "").Replace("```", "").Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call Gemini for GenerateStructuredBriefAsync. Returning mock JSON.");
                return @"{
  ""project_name"": ""Auto-Generated Mock Project"",
  ""client_name"": ""Auto Client"",
  ""business_goal"": ""Workflow testing fallback."",
  ""target_users"": [""Developers"", ""Clients""],
  ""features"": [""Mock feature 1""],
  ""platforms"": [""Web""],
  ""design_requirements"": [],
  ""technical_constraints"": [],
  ""timeline"": ""TBD"",
  ""budget"": ""TBD"",
  ""missing_information"": [""Need real Gemini Key""]
}";
            }
        }

        public async Task<string> DetectMissingInformationAsync(string structuredJson)
        {
            _logger.LogInformation("Detecting missing information using LLM.");

            var systemPrompt = @"You are a brief validation system. Look at the structured project brief JSON. 
Identify any fields that are deeply crucial but currently empty or vague (e.g., empty budget, empty timeline, no target users).
Return ONLY a JSON array of strings representing the missing fields you recommend asking the client about.
Example: [""budget"", ""timeline""]";

            try
            {
                var responseText = await CallGeminiAsync(systemPrompt, structuredJson);
                return responseText.Replace("```json", "").Replace("```", "").Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call Gemini for DetectMissingInformationAsync. Returning empty array.");
                return "[]";
            }
        }

        private async Task<string> CallGeminiAsync(string systemPrompt, string userPrompt, string? mimeType = null, byte[]? inlineData = null)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";
            
            using var client = new HttpClient();
            
            var parts = new List<object>();

            // For videos, Google requires their File API upload. We must upload it first, poll for active state, then attach File URI
            string? uploadedFileUri = null;
            if (inlineData != null && mimeType == "video/mp4")
            {
                var uploadUrl = $"https://generativelanguage.googleapis.com/upload/v1beta/files?uploadType=media&key={_apiKey}";
                var content = new ByteArrayContent(inlineData);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                var uploadRes = await client.PostAsync(uploadUrl, content);
                uploadRes.EnsureSuccessStatusCode();
                var uploadJson = await uploadRes.Content.ReadFromJsonAsync<JsonDocument>();
                uploadedFileUri = uploadJson?.RootElement.GetProperty("file").GetProperty("uri").GetString();
                var fileName = uploadJson?.RootElement.GetProperty("file").GetProperty("name").GetString();
                
                // Wait for video to process on Google side before continuing
                if (!string.IsNullOrEmpty(fileName))
                {
                    var fileCheckUrl = $"https://generativelanguage.googleapis.com/v1beta/{fileName}?key={_apiKey}";
                    bool isActive = false;
                    for(int i=0; i<30; i++) // 60 seconds max wait
                    {
                        await Task.Delay(2000);
                        var checkRes = await client.GetAsync(fileCheckUrl);
                        if(checkRes.IsSuccessStatusCode)
                        {
                            var checkJson = await checkRes.Content.ReadFromJsonAsync<JsonDocument>();
                            var state = checkJson?.RootElement.GetProperty("state").GetString();
                            if(state == "ACTIVE") { isActive = true; break; }
                            if(state == "FAILED") throw new Exception("Video processing failed on Google servers");
                        }
                    }
                    if(!isActive) throw new Exception("Video took too long to process");
                }
            }

            if (!string.IsNullOrEmpty(userPrompt))
                parts.Add(new { text = userPrompt });
                
            if (inlineData != null && !string.IsNullOrEmpty(mimeType))
            {
                if(mimeType == "video/mp4" && uploadedFileUri != null)
                {
                    parts.Add(new { file_data = new { mime_type = mimeType, file_uri = uploadedFileUri } });
                }
                else
                {
                    parts.Add(new {
                        inline_data = new {
                            mime_type = mimeType,
                            data = Convert.ToBase64String(inlineData)
                        }
                    });
                }
            }

            var payload = new 
            {
                system_instruction = new {
                    parts = new[] { new { text = systemPrompt } }
                },
                contents = new[] {
                    new { role = "user", parts = parts }
                }
            };

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
            var response = await client.PostAsJsonAsync(url, payload, jsonOptions);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini API Error: {StatusCode} - {ErrorBody}", response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }
            
            var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var resultText = jsonDoc?.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text").GetString();

            return resultText ?? string.Empty;
        }
    }
}