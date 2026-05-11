using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using UglyToad.PdfPig; 
using System.Text;

namespace BriefGenerator.Api.Services
{
    public class AiProcessingService : IAiProcessingService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AiProcessingService> _logger;
        private readonly ChatClient _chatClient;
        private readonly AudioClient _audioClient;

        public AiProcessingService(AppDbContext context, IConfiguration configuration, ILogger<AiProcessingService> logger)
        {
            _context = context;
            _logger = logger;
            
            string apiKey = configuration["OpenAIApiKey"] ?? throw new InvalidOperationException("OpenAI API key missing");
            
            // Central OpenAI Client
            var openAiClient = new OpenAIClient(apiKey);
            
            // specific clients for tasks
            _chatClient = openAiClient.GetChatClient("gpt-4o-mini"); 
            _audioClient = openAiClient.GetAudioClient("whisper-1");
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

            intakeSession.Status = "generated";
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

                // 2. Audio (Whisper)
                if (extension is ".mp3" or ".wav" or ".m4a" or ".ogg")
                {
                    _logger.LogInformation("Transcribing audio file: {FileName}", file.FileName);
                    AudioTranscription transcription = await _audioClient.TranscribeAudioAsync(file.StoragePath);
                    return transcription.Text;
                }

                // 3. Images (OCR via GPT-4 Vision)
                if (extension is ".png" or ".jpg" or ".jpeg" or ".webp")
                {
                    _logger.LogInformation("Extracting text from image: {FileName}", file.FileName);
                    
                    var imageBytes = await File.ReadAllBytesAsync(file.StoragePath);
                    var mimeType = extension == ".png" ? "image/png" : 
                                   extension == ".webp" ? "image/webp" : "image/jpeg";

                    var imagePart = ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), mimeType);
                    var textPart = ChatMessageContentPart.CreateTextPart("Extract all readable text from this image. If it's a diagram or sketch, describe the contents and flow in detail.");
                    
                    var response = await _chatClient.CompleteChatAsync(new UserChatMessage(textPart, imagePart));
                    return response.Value.Content[0].Text;
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

            var response = await _chatClient.CompleteChatAsync(new SystemChatMessage(systemPrompt), new UserChatMessage($"Raw Text:\n{extractedText}"));
            return response.Value.Content[0].Text.Replace("```json", "").Replace("```", "").Trim();
        }

        public async Task<string> DetectMissingInformationAsync(string structuredJson)
        {
            _logger.LogInformation("Detecting missing information using LLM.");

            var systemPrompt = @"You are a brief validation system. Look at the structured project brief JSON. 
Identify any fields that are deeply crucial but currently empty or vague (e.g., empty budget, empty timeline, no target users).
Return ONLY a JSON array of strings representing the missing fields you recommend asking the client about.
Example: [""budget"", ""timeline""]";

            var response = await _chatClient.CompleteChatAsync(new SystemChatMessage(systemPrompt), new UserChatMessage(structuredJson));
            return response.Value.Content[0].Text.Replace("```json", "").Replace("```", "").Trim();
        }
    }
}