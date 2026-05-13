using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using BriefGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

namespace BriefGenerator.Api.Controllers
{
    [ApiController]
    [Route("api/public/brief")]
    public class PublicBriefController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public PublicBriefController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // Internally used by the employee to generate the client-facing link
        [HttpPost("generate-link/{briefId}")]
        public async Task<IActionResult> GenerateLink(Guid briefId)
        {
            var brief = await _context.Briefs.FindAsync(briefId);
            if (brief == null) return NotFound("Brief not found.");

            var existing = await _context.ShareLinks
                .Where(s => s.BriefId == briefId && s.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.ExpiresAt)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return Ok(new { url = $"/api/public/brief/{existing.Token}", token = existing.Token, expiresAt = existing.ExpiresAt });
            }

            var shareLink = new ShareLink
            {
                Id = Guid.NewGuid(),
                BriefId = briefId,
                Token = Guid.NewGuid().ToString("N"), // Cryptographically random token
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _context.ShareLinks.Add(shareLink);
            await _context.SaveChangesAsync();

            return Ok(new { url = $"/api/public/brief/{shareLink.Token}", token = shareLink.Token, expiresAt = shareLink.ExpiresAt });
        }

        [HttpGet("{token}")]
        public async Task<IActionResult> GetPublicBrief(string token)
        {
            var shareLink = await _context.ShareLinks.Include(s => s.Brief).FirstOrDefaultAsync(s => s.Token == token);
            if (shareLink == null || shareLink.ExpiresAt < DateTime.UtcNow)
                return NotFound(new { error = "Invalid or expired link" });

            return Ok(shareLink.Brief);
        }

        [HttpPost("{token}/answers")]
        public async Task<IActionResult> SubmitAnswers(string token, [FromBody] SubmitAnswersRequest request)
        {
            var shareLink = await _context.ShareLinks
                .Include(s => s.Brief)
                .FirstOrDefaultAsync(s => s.Token == token);
            if (shareLink == null || shareLink.ExpiresAt < DateTime.UtcNow)
                return NotFound(new { error = "Invalid or expired link" });

            var responses = request.Responses.Select(resp => new ClientResponse
            {
                Id = Guid.NewGuid(),
                BriefId = shareLink.BriefId,
                FieldName = resp.Field,
                ResponseText = resp.Value
            }).ToList();

            _context.ClientResponses.AddRange(responses);

            // Mark brief/intake as having client feedback (employee should review/merge).
            if (shareLink.Brief != null)
            {
                shareLink.Brief.Status = "client_responded";
                var intake = await _context.IntakeSessions.FindAsync(shareLink.Brief.IntakeSessionId);
                if (intake != null)
                {
                    intake.Status = "client_responded";
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Answers submitted successfully." });
        }

        [HttpPost("{token}/confirm")]
        public async Task<IActionResult> ConfirmBrief(string token)
        {
            var shareLink = await _context.ShareLinks.Include(s => s.Brief).FirstOrDefaultAsync(s => s.Token == token);
            if (shareLink == null || shareLink.ExpiresAt < DateTime.UtcNow)
                return NotFound(new { error = "Invalid or expired link" });

            shareLink.Brief.Status = "confirmed";
            
            var intake = await _context.IntakeSessions.FindAsync(shareLink.Brief.IntakeSessionId);
            if (intake != null)
            {
                intake.Status = "confirmed";
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Brief confirmed." });
        }

        /// <summary>
        /// Generates the AI's opening greeting message based on the actual brief content.
        /// Called once when the client first opens the share link.
        /// </summary>
        [HttpGet("{token}/chat/greeting")]
        public async Task<IActionResult> GetGreeting(string token)
        {
            var shareLink = await _context.ShareLinks
                .Include(s => s.Brief)
                .FirstOrDefaultAsync(s => s.Token == token);

            if (shareLink == null || shareLink.ExpiresAt < DateTime.UtcNow)
                return NotFound(new { error = "Invalid or expired link" });

            var brief = shareLink.Brief;

            var greetingPrompt = $@"You are a friendly and professional project requirements assistant for a software/design agency.

A client has just opened their project brief for review. Here is the brief:
{brief.StructuredJson}

Missing or vague fields identified by the system:
{brief.MissingFieldsJson}

Your task: Write a SHORT, warm, personalised opening message (3-5 sentences max) that:
1. Greets the client and references the actual project by name
2. Briefly summarises what the brief covers so far (1 sentence)
3. Proactively mentions 1-2 specific things that could be improved, clarified, or added — be concrete, not generic
4. Ends with ONE specific question about the most important missing or vague piece

Do NOT use bullet points. Write in a natural, conversational tone. Do NOT start with ""Hi there"" — be more specific.
Do NOT include any ##FIELD_UPDATE## markers in this greeting.";

            try
            {
                var apiKey = _configuration["GeminiApiKey"] ?? throw new InvalidOperationException("Gemini API key missing");
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

                var payload = new
                {
                    system_instruction = new { parts = new[] { new { text = "You are a concise, warm project requirements assistant." } } },
                    contents = new[]
                    {
                        new { role = "user", parts = new[] { new { text = greetingPrompt } } }
                    }
                };

                using var httpClient = new HttpClient();
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
                var response = await httpClient.PostAsJsonAsync(url, payload, jsonOptions);

                if (!response.IsSuccessStatusCode)
                    return StatusCode(502, new { error = "AI service error" });

                var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
                var greeting = jsonDoc?.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString() ?? string.Empty;

                return Ok(new { greeting = greeting.Trim() });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Greeting failed", detail = ex.Message });
            }
        }

        /// <summary>
        /// Interactive AI chat endpoint for the client share page.
        /// The AI proactively leads the conversation — suggesting improvements and asking about missing info.
        /// </summary>
        [HttpPost("{token}/chat")]
        public async Task<IActionResult> Chat(string token, [FromBody] ChatRequest request)
        {
            var shareLink = await _context.ShareLinks
                .Include(s => s.Brief)
                .FirstOrDefaultAsync(s => s.Token == token);

            if (shareLink == null || shareLink.ExpiresAt < DateTime.UtcNow)
                return NotFound(new { error = "Invalid or expired link" });

            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest(new { error = "Message cannot be empty." });

            var brief = shareLink.Brief;

            var systemPrompt = $@"You are a friendly and professional project requirements assistant for a software/design agency.

Your job is to have a proactive, natural conversation with the CLIENT to help them refine and complete their project brief.

Here is the current project brief:
{brief.StructuredJson}

Missing or vague fields identified:
{brief.MissingFieldsJson}

Your behavior rules:
1. Be warm, concise, and conversational — not robotic or formal.
2. Be PROACTIVE: don't just answer questions, actively suggest improvements, flag vague requirements, and propose additions the client may not have thought of.
3. Ask ONE question at a time. Never ask multiple questions in one message.
4. When the client answers, acknowledge it naturally, then either dig deeper or move to the next gap.
5. If you notice something in the brief that seems incomplete or could cause problems later (e.g. no timeline, vague features, missing platform info), bring it up even if the client didn't ask.
6. If the client seems satisfied and all gaps are covered, suggest they confirm the brief.
7. Keep responses SHORT (2-4 sentences) unless the client asks for detail.
8. At the END of your reply, if the client's message contained a concrete answer to a missing field,
   append this on its own line (do not include it otherwise):
   ##FIELD_UPDATE##{{""field"": ""field_name"", ""value"": ""extracted value""}}

Valid field names: project_name, client_name, business_goal, target_users, features, platforms, design_requirements, technical_constraints, timeline, budget.";

            try
            {
                var apiKey = _configuration["GeminiApiKey"] ?? throw new InvalidOperationException("Gemini API key missing");
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

                // Build conversation history for Gemini multi-turn format
                var contents = new List<object>();
                foreach (var msg in request.History ?? new List<ChatMessage>())
                {
                    contents.Add(new
                    {
                        role = msg.Role == "assistant" ? "model" : "user",
                        parts = new[] { new { text = msg.Content } }
                    });
                }
                // Add the new user message
                contents.Add(new
                {
                    role = "user",
                    parts = new[] { new { text = request.Message } }
                });

                var payload = new
                {
                    system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                    contents
                };

                using var httpClient = new HttpClient();
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
                var response = await httpClient.PostAsJsonAsync(url, payload, jsonOptions);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    return StatusCode(502, new { error = "AI service error", detail = err });
                }

                var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
                var rawReply = jsonDoc?.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString() ?? string.Empty;

                // Parse out any ##FIELD_UPDATE## marker
                string? fieldName = null;
                string? fieldValue = null;
                string cleanReply = rawReply;

                var markerIndex = rawReply.IndexOf("##FIELD_UPDATE##", StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    cleanReply = rawReply[..markerIndex].Trim();
                    var jsonPart = rawReply[(markerIndex + "##FIELD_UPDATE##".Length)..].Trim();
                    try
                    {
                        var update = JsonSerializer.Deserialize<JsonElement>(jsonPart);
                        fieldName = update.GetProperty("field").GetString();
                        fieldValue = update.GetProperty("value").GetString();

                        // Persist the extracted answer as a ClientResponse
                        if (!string.IsNullOrWhiteSpace(fieldName) && !string.IsNullOrWhiteSpace(fieldValue))
                        {
                            _context.ClientResponses.Add(new ClientResponse
                            {
                                Id = Guid.NewGuid(),
                                BriefId = brief.Id,
                                FieldName = fieldName,
                                ResponseText = fieldValue
                            });
                            brief.Status = "client_responded";
                            var intake = await _context.IntakeSessions.FindAsync(brief.IntakeSessionId);
                            if (intake != null) intake.Status = "client_responded";
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch { /* ignore parse errors on the marker */ }
                }

                return Ok(new
                {
                    reply = cleanReply,
                    fieldUpdated = fieldName,
                    fieldValue
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Chat failed", detail = ex.Message });
            }
        }

        // ── Private Gemini helper (multi-turn) ──────────────────────────────
        private static async Task<string> CallGeminiChatAsync(
            string apiKey, string systemPrompt, List<object> contents)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var payload = new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents
            };
            using var client = new HttpClient();
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = null };
            var response = await client.PostAsJsonAsync(url, payload, opts);
            response.EnsureSuccessStatusCode();
            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
            return doc?.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text").GetString() ?? string.Empty;
        }
    }

    // ── Request / Response DTOs ──────────────────────────────────────────────

    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty;    // "user" | "assistant"
        public string Content { get; set; } = string.Empty;
    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public List<ChatMessage>? History { get; set; }
    }

    public class SubmitAnswersRequest
    {
        public List<ClientAnswer> Responses { get; set; } = new();
    }

    public class ClientAnswer
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}