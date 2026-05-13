using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using BriefGenerator.Web.Models;

namespace BriefGenerator.Web.Controllers;

public class HomeController : Controller
{
    private readonly IHttpClientFactory _clientFactory;

    public HomeController(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    // GET: / — Dashboard
    public async Task<IActionResult> Index()
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var response = await client.GetAsync("api/briefs");

        var briefs = new List<BriefDto>();
        if (response.IsSuccessStatusCode)
        {
            briefs = await response.Content.ReadFromJsonAsync<List<BriefDto>>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }

        return View(briefs);
    }

    // GET: /Home/Details/{id}
    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var client = _clientFactory.CreateClient("ApiClient");

        var briefResponse = await client.GetAsync($"api/briefs/{id}");
        if (!briefResponse.IsSuccessStatusCode) return NotFound();

        var brief = await briefResponse.Content.ReadFromJsonAsync<BriefDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (brief == null) return NotFound();

        StructuredBriefData? structured = null;
        if (!string.IsNullOrWhiteSpace(brief.StructuredJson))
        {
            try
            {
                structured = JsonSerializer.Deserialize<StructuredBriefData>(
                    brief.StructuredJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { }
        }

        var missingFields = ParseMissingFields(brief.MissingFieldsJson);

        ShareLinkDto? shareLink = null;
        var shareLinkResponse = await client.GetAsync($"api/briefs/{id}/share-link");
        if (shareLinkResponse.IsSuccessStatusCode)
        {
            shareLink = await shareLinkResponse.Content.ReadFromJsonAsync<ShareLinkDto>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        List<ClientResponseDto> clientResponses = new();
        var responsesResp = await client.GetAsync($"api/briefs/{id}/responses");
        if (responsesResp.IsSuccessStatusCode)
        {
            clientResponses = await responsesResp.Content.ReadFromJsonAsync<List<ClientResponseDto>>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }

        ViewBag.Structured = structured;
        ViewBag.MissingFields = missingFields;
        ViewBag.ShareLink = shareLink;
        ViewBag.ClientResponses = clientResponses;

        return View(brief);
    }

    // GET: /Home/CreateIntake
    [HttpGet]
    public IActionResult CreateIntake() => View();

    // POST: /Home/CreateIntake
    [HttpPost]
    public async Task<IActionResult> CreateIntake(IntakeViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            ModelState.AddModelError("Title", "Project title is required.");
            return View(model);
        }

        var client = _clientFactory.CreateClient("ApiClient");
        using var content = new MultipartFormDataContent();

        content.Add(new StringContent(model.Title.Trim()), "Title");

        if (!string.IsNullOrEmpty(model.Notes))
            content.Add(new StringContent(model.Notes), "Notes");

        if (model.Files != null)
        {
            foreach (var file in model.Files)
            {
                var streamContent = new StreamContent(file.OpenReadStream());
                streamContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                content.Add(streamContent, "Files", file.FileName);
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync("api/intake/create", content);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"Could not reach the API: {ex.Message}");
            return View(model);
        }

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var intakeId = result.GetProperty("intakeId").GetString();
            return RedirectToAction("Processing", new { id = intakeId });
        }

        var errorBody = await response.Content.ReadAsStringAsync();
        ModelState.AddModelError(string.Empty, $"Failed ({(int)response.StatusCode}): {errorBody}");
        return View(model);
    }

    // GET: /Home/Processing/{id}
    [HttpGet]
    public IActionResult Processing(string id)
    {
        ViewBag.IntakeId = id;
        return View();
    }

    // GET: /Home/IntakeStatus/{id} — AJAX polling
    [HttpGet]
    public async Task<IActionResult> IntakeStatus(string id)
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var response = await client.GetAsync($"api/intake/{id}");
        if (!response.IsSuccessStatusCode)
            return Json(new { status = "error" });

        var data = await response.Content.ReadFromJsonAsync<IntakeStatusDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return Json(new
        {
            status = data?.Status,
            briefId = data?.Brief?.Id,
            shareToken = data?.ShareToken
        });
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    // ── Shared helper ─────────────────────────────────────────────────────
    /// <summary>
    /// Parses MissingFieldsJson which Gemini returns as either:
    ///   ["budget", "timeline"]
    ///   or [{"clarifying_question": "...", "reason": "..."}, ...]
    /// Returns a flat list of strings in both cases.
    /// </summary>
    internal static List<string> ParseMissingFields(string? json)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            var elements = JsonSerializer.Deserialize<List<JsonElement>>(json);
            if (elements == null) return result;

            foreach (var el in elements)
            {
                switch (el.ValueKind)
                {
                    case JsonValueKind.String:
                        var str = el.GetString();
                        if (!string.IsNullOrWhiteSpace(str)) result.Add(str);
                        break;

                    case JsonValueKind.Object:
                        // Try common property names Gemini uses
                        foreach (var key in new[] { "clarifying_question", "question", "field", "name" })
                        {
                            if (el.TryGetProperty(key, out var prop) &&
                                prop.ValueKind == JsonValueKind.String)
                            {
                                var s = prop.GetString();
                                if (!string.IsNullOrWhiteSpace(s)) { result.Add(s); break; }
                            }
                        }
                        break;
                }
            }
        }
        catch { }

        return result;
    }
}
