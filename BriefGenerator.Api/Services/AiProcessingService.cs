using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;

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
                IntakeSessionId = intakeId,
                StructuredJson = structuredBrief,
                MissingFieldsJson = missingInfo,
                MarkdownOutput = "Markdown representation goes here...", 
                Status = "awaiting_client"
            };

            intakeSession.Status = "generated";
            _context.Briefs.Add(brief);
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
                    
                    var response = await _chatClient.CompleteChatAsync(
                        new UserChatMessage(textPart, imagePart)
                    );

                    return response.Value.Content[0].Text;
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

            var systemPrompt = @"You are an expert product manager. Extract information from the provided raw text and output a JSON object describing the project brief.
Always use this exact JSON structure:
{
  ""project_name"": """",
  ""client_name"": """",
  ""business_goal"": """",
  ""target_users"": [],
  ""features"": [],
  ""platforms"": [],
  ""design_requirements"": [],
  ""technical_constraints"": [],
  ""timeline"": """",
  ""budget"": """",
  ""missing_information"": []
}
If any piece of information is missing from the raw text, leave the string empty or the array empty.";

            var response = await _chatClient.CompleteChatAsync(
                new SystemChatMessage(systemPrompt),
                new UserChatMessage($"Raw Text:\n{extractedText}")
            );

            var content = response.Value.Content[0].Text;
            content = content.Replace("```json", "").Replace("```", "").Trim();
            
            return content;
        }

        public async Task<string> DetectMissingInformationAsync(string structuredJson)
        {
            _logger.LogInformation("Detecting missing information using LLM.");

            var systemPrompt = @"You are a brief validation system. Look at the structured project brief JSON. 
Identify any fields that are deeply crucial but currently empty or vague (e.g., empty budget, empty timeline, no target users).
Return ONLY a JSON array of strings representing the missing fields you recommend asking the client about.
Example: [""budget"", ""timeline""]";

            var response = await _chatClient.CompleteChatAsync(
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(structuredJson)
            );

            var content = response.Value.Content[0].Text;
            content = content.Replace("```json", "").Replace("```", "").Trim();

            return content;
        }
    }
}