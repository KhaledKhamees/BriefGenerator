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

            var greetingPrompt = $@"You are a project requirements assistant.

A client just opened their project brief. Here is the brief:
{brief.StructuredJson}

These are the ONLY fields that need to be collected:
{brief.MissingFieldsJson}

Write a greeting of MAX 2 sentences:
1. Greet them by project name.
2. Ask directly about the FIRST field in the missing fields list above — nothing else.

Be short and direct. No bullet points. Do not mention any other topics.";

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

                return Ok(new { greeting = greeting.Trim(), missingFieldNames = ExtractMissingFieldNames(brief.MissingFieldsJson) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Greeting failed", detail = ex.Message });
            }
        }

        /// <summary>
        /// Interactive AI chat for the client share page.
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

            var systemPrompt = $@"You are a friendly project requirements assistant helping a client review and complete their project brief.

Here is the current brief:
{brief.StructuredJson}

These fields are currently missing or incomplete:
{brief.MissingFieldsJson}

Your job:
- Have a natural, friendly conversation with the client.
- Naturally work through the missing fields above — ask about them conversationally, one at a time.
- Keep replies short (2-3 sentences max).
- When the client provides information that fills a missing field, acknowledge it warmly and move to the next one.
- When all missing fields are covered, let the client know and suggest confirming the brief.
- If the client asks about anything in the brief, answer clearly and helpfully.

When the client's message contains a clear answer to one of these fields, append this on a new line at the very end of your reply (nothing after it):
##FIELD_UPDATE##{{""field"": ""field_name"", ""value"": ""the value""}}

Valid field names: project_name, client_name, business_goal, target_users, features, platforms, design_requirements, technical_constraints, timeline, budget.";

            try
            {
                var apiKey = _configuration["GeminiApiKey"] ?? throw new InvalidOperationException("Gemini API key missing");
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";

                var contents = new List<object>();
                foreach (var msg in request.History ?? new List<ChatMessage>())
                {
                    contents.Add(new
                    {
                        role = msg.Role == "assistant" ? "model" : "user",
                        parts = new[] { new { text = msg.Content } }
                    });
                }
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

                string? fieldName = null;
                string? fieldValue = null;
                string cleanReply = rawReply;

                var markerIndex = rawReply.IndexOf("##FIELD_UPDATE##", StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    cleanReply = rawReply[..markerIndex].Trim();
                    var afterMarker = rawReply[(markerIndex + "##FIELD_UPDATE##".Length)..].Trim();
                    var jsonStart = afterMarker.IndexOf('{');
                    var jsonEnd   = afterMarker.LastIndexOf('}');

                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        try
                        {
                            var update = JsonSerializer.Deserialize<JsonElement>(afterMarker[jsonStart..(jsonEnd + 1)]);
                            fieldName  = update.GetProperty("field").GetString()?.Trim();
                            fieldValue = update.GetProperty("value").GetString()?.Trim();

                            if (!string.IsNullOrWhiteSpace(fieldName) && !string.IsNullOrWhiteSpace(fieldValue))
                            {
                                var existing = await _context.ClientResponses
                                    .FirstOrDefaultAsync(r => r.BriefId == brief.Id && r.FieldName == fieldName);
                                if (existing != null)
                                    existing.ResponseText = fieldValue;
                                else
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
                        catch (Exception parseEx)
                        {
                            Console.WriteLine($"[Chat] parse failed: {parseEx.Message} | raw: {afterMarker}");
                        }
                    }
                }

                return Ok(new { reply = cleanReply, fieldUpdated = fieldName, fieldValue });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Chat failed", detail = ex.Message });
            }
        }

        /// <summary>
        /// Extracts canonical field names from MissingFieldsJson.
        /// Handles both ["budget","timeline"] and [{"clarifying_question":"..."}] shapes.
        /// Falls back to matching known field names from the brief JSON.
        /// </summary>
        private static List<string> ExtractMissingFieldNames(string? missingFieldsJson)
        {
            var knownFields = new[] { "project_name", "client_name", "business_goal", "target_users",
                "features", "platforms", "design_requirements", "technical_constraints", "timeline", "budget" };

            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(missingFieldsJson)) return result;

            try
            {
                var elements = JsonSerializer.Deserialize<List<JsonElement>>(missingFieldsJson);
                if (elements == null) return result;

                foreach (var el in elements)
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString()?.Trim().ToLower().Replace(" ", "_");
                        if (!string.IsNullOrWhiteSpace(s) && knownFields.Contains(s))
                            result.Add(s);
                    }
                    else if (el.ValueKind == JsonValueKind.Object)
                    {
                        string? found = null;

                        // Try explicit field key first
                        foreach (var key in new[] { "field", "field_name", "name" })
                        {
                            if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                            {
                                var s = prop.GetString()?.Trim().ToLower().Replace(" ", "_");
                                if (!string.IsNullOrWhiteSpace(s) && knownFields.Contains(s))
                                { found = s; break; }
                            }
                        }

                        // Infer from question text
                        if (found == null)
                        {
                            foreach (var key in new[] { "clarifying_question", "question" })
                            {
                                if (el.TryGetProperty(key, out var q) && q.ValueKind == JsonValueKind.String)
                                {
                                    var text = q.GetString()?.ToLower() ?? "";
                                    foreach (var field in knownFields)
                                    {
                                        if (text.Contains(field.Replace("_", " ")) || text.Contains(field))
                                        { found = field; break; }
                                    }
                                    break;
                                }
                            }
                        }

                        if (found != null) result.Add(found);
                    }
                }
            }
            catch { }

            return result.Distinct().ToList();
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