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

        List<string> missingFields = new();
        if (!string.IsNullOrWhiteSpace(brief.MissingFieldsJson))
        {
            try
            {
                missingFields = JsonSerializer.Deserialize<List<string>>(brief.MissingFieldsJson) ?? new();
            }
            catch { }
        }

        var vm = new PublicBriefViewModel
        {
            Token = token,
            Brief = brief,
            Structured = structured,
            MissingFields = missingFields
        };

        return base.View("ClientView", vm);
    }

    // POST: /brief/{token}/answers — Client submits missing field answers
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

    // POST: /brief/{token}/confirm — Client confirms the brief
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

    // POST: /brief/{token}/chat — Proxy to the API chat endpoint (avoids browser CORS)
    [HttpPost("{token}/chat")]
    public async Task<IActionResult> Chat(string token, [FromBody] JsonElement body)
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"api/public/brief/{token}/chat", content);
        var responseBody = await response.Content.ReadAsStringAsync();
        return Content(responseBody, "application/json");
    }

    // GET: /brief/{token}/chat/greeting — Proxy for AI-generated opening message
    [HttpGet("{token}/chat/greeting")]
    public async Task<IActionResult> ChatGreeting(string token)
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var response = await client.GetAsync($"api/public/brief/{token}/chat/greeting");
        var responseBody = await response.Content.ReadAsStringAsync();
        return Content(responseBody, "application/json");
    }

    // GET: /brief/{token}/confirmed — Thank you page
    [HttpGet("{token}/confirmed")]
    public IActionResult Confirmed(string token)
    {
        ViewBag.Token = token;
        return View();
    }
}
