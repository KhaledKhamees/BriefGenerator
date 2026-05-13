using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BriefGenerator.Web.Models;
using BriefGenerator.Web.Services;

namespace BriefGenerator.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AuthenticatedApiClient _api;

    public HomeController(AuthenticatedApiClient api)
    {
        _api = api;
    }

    // GET: / — Dashboard
    public async Task<IActionResult> Index()
    {
        var client = _api.CreateClient();
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
        var client = _api.CreateClient();

        var briefResponse = await client.GetAsync($"api/briefs/{id}");
        if (!briefResponse.IsSuccessStatusCode)
            return NotFound();

        var brief = await briefResponse.Content.ReadFromJsonAsync<BriefDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (brief == null) return NotFound();

        StructuredBriefData? structured = null;
        if (!string.IsNullOrWhiteSpace(brief.StructuredJson))
        {
            try { structured = JsonSerializer.Deserialize<StructuredBriefData>(brief.StructuredJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch { }
        }

        List<string> missingFields = new();
        if (!string.IsNullOrWhiteSpace(brief.MissingFieldsJson))
        {
            try { missingFields = JsonSerializer.Deserialize<List<string>>(brief.MissingFieldsJson) ?? new(); }
            catch { }
        }

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
        // Title is required — catch it before hitting the API
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            ModelState.AddModelError("Title", "Project title is required.");
            return View(model);
        }

        var client = _api.CreateClient();
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

        // Surface the actual API error so we can diagnose it
        var errorBody = await response.Content.ReadAsStringAsync();
        var errorMsg = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Authentication failed. Please sign out and sign in again.",
            System.Net.HttpStatusCode.Forbidden    => "You don't have permission to create intakes.",
            _ => $"API error ({(int)response.StatusCode}): {errorBody}"
        };

        ModelState.AddModelError(string.Empty, errorMsg);
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
        var client = _api.CreateClient();
        var response = await client.GetAsync($"api/intake/{id}");
        if (!response.IsSuccessStatusCode)
            return Json(new { status = "error" });

        var data = await response.Content.ReadFromJsonAsync<IntakeStatusDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return Json(new { status = data?.Status, briefId = data?.Brief?.Id, shareToken = data?.ShareToken });
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
