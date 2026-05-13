using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using BriefGenerator.Web.Models;

namespace BriefGenerator.Web.Controllers;

[Route("brief")]
public class PublicBriefController : Controller
{
    private readonly IHttpClientFactory _clientFactory;

    public PublicBriefController(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    // GET: /brief/{token} — Public client view
    [HttpGet("{token}")]
    public async Task<IActionResult> ClientBriefView(string token)
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var response = await client.GetAsync($"api/public/brief/{token}");

        if (!response.IsSuccessStatusCode)
            return base.View("InvalidLink");

        var brief = await response.Content.ReadFromJsonAsync<BriefDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (brief == null)
            return base.View("InvalidLink");

        StructuredBriefData? structured = null;
        if (!string.IsNullOrWhiteSpace(brief.StructuredJson))
        {
            try
            {
                structured = JsonSerializer.Deserialize<StructuredBriefData>(brief.StructuredJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { }
        }

        // Handles both List<string> and List<{clarifying_question,...}> shapes
        var missingFields = HomeController.ParseMissingFields(brief.MissingFieldsJson);

        // Canonical field names in the same order (for data-field attributes in the view)
        var missingFieldNames = ParseCanonicalFieldNames(brief.MissingFieldsJson, missingFields);

        var vm = new PublicBriefViewModel
        {
            Token = token,
            Brief = brief,
            Structured = structured,
            MissingFields = missingFields,
            MissingFieldNames = missingFieldNames
        };

        return base.View("ClientView", vm);
    }

    // POST: /brief/{token}/answers
    [HttpPost("{token}/answers")]
    public async Task<IActionResult> SubmitAnswers(string token, [FromForm] SubmitAnswersViewModel model)
    {
        var client = _clientFactory.CreateClient("ApiClient");

        var payload = new
        {
            responses = model.Answers
                .Where(a => !string.IsNullOrWhiteSpace(a.Value))
                .Select(a => new { field = a.Field, value = a.Value })
                .ToList()
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"api/public/brief/{token}/answers", content);

        if (response.IsSuccessStatusCode)
            return RedirectToAction("Confirmed", new { token });

        TempData["Error"] = "Failed to submit answers. Please try again.";
        return RedirectToAction("ClientBriefView", new { token });
    }

    // POST: /brief/{token}/confirm
    [HttpPost("{token}/confirm")]
    public async Task<IActionResult> Confirm(string token)
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var response = await client.PostAsync($"api/public/brief/{token}/confirm",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        if (response.IsSuccessStatusCode)
            return RedirectToAction("Confirmed", new { token });

        TempData["Error"] = "Failed to confirm brief. Please try again.";
        return RedirectToAction("ClientBriefView", new { token });
    }

    // POST: /brief/{token}/chat — Proxy (avoids browser CORS)
    [HttpPost("{token}/chat")]
    public async Task<IActionResult> Chat(string token, [FromBody] JsonElement body)
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"api/public/brief/{token}/chat", content);
        var responseBody = await response.Content.ReadAsStringAsync();
        return Content(responseBody, "application/json");
    }

    // GET: /brief/{token}/chat/greeting — Proxy for AI opening message
    [HttpGet("{token}/chat/greeting")]
    public async Task<IActionResult> ChatGreeting(string token)
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var response = await client.GetAsync($"api/public/brief/{token}/chat/greeting");
        var responseBody = await response.Content.ReadAsStringAsync();
        return Content(responseBody, "application/json");
    }

    // GET: /brief/{token}/confirmed
    [HttpGet("{token}/confirmed")]
    public IActionResult Confirmed(string token)
    {
        ViewBag.Token = token;
        return View();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Maps display strings back to canonical field names.
    /// If the JSON contains plain field names like ["budget","timeline"] those are used directly.
    /// If it contains question objects, we try to infer the field name from the question text.
    /// Falls back to the index position if nothing matches.
    /// </summary>
    private static List<string> ParseCanonicalFieldNames(string? json, List<string> displayStrings)
    {
        var known = new[] { "project_name", "client_name", "business_goal", "target_users",
            "features", "platforms", "design_requirements", "technical_constraints", "timeline", "budget" };

        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            var elements = JsonSerializer.Deserialize<List<JsonElement>>(json);
            if (elements == null) return result;

            foreach (var el in elements)
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    // Already a field name
                    var s = el.GetString()?.Trim().ToLower().Replace(" ", "_") ?? "";
                    result.Add(known.Contains(s) ? s : s);
                }
                else if (el.ValueKind == JsonValueKind.Object)
                {
                    // Try explicit field key first
                    string? found = null;
                    foreach (var key in new[] { "field", "field_name", "name" })
                    {
                        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
                        {
                            var s = p.GetString()?.Trim().ToLower().Replace(" ", "_") ?? "";
                            if (known.Contains(s)) { found = s; break; }
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
                                foreach (var f in known)
                                {
                                    if (text.Contains(f.Replace("_", " ")) || text.Contains(f))
                                    { found = f; break; }
                                }
                                break;
                            }
                        }
                    }
                    result.Add(found ?? $"field_{result.Count}");
                }
            }
        }
        catch { }

        return result;
    }
}
