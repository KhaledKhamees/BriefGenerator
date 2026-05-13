using System.Diagnostics;
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

    public async Task<IActionResult> Index()
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var response = await client.GetAsync("api/briefs");

        List<BriefDto> briefs = new List<BriefDto>();
        if (response.IsSuccessStatusCode)
        {
            briefs = await response.Content.ReadFromJsonAsync<List<BriefDto>>() ?? new List<BriefDto>();
        }

        return View(briefs);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var client = _clientFactory.CreateClient("ApiClient");
        var response = await client.GetAsync($"api/briefs/{id}");

        if (response.IsSuccessStatusCode)
        {
            var brief = await response.Content.ReadFromJsonAsync<BriefDto>();
            return View(brief);
        }

        return NotFound();
    }

    [HttpGet]
    public IActionResult CreateIntake()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> CreateIntake(IntakeViewModel model)
    {
        var client = _clientFactory.CreateClient("ApiClient");
        using var content = new MultipartFormDataContent();

        if (!string.IsNullOrEmpty(model.Title))
        {
            content.Add(new StringContent(model.Title), "Title");
        }

        if (!string.IsNullOrEmpty(model.Notes))
        {
            content.Add(new StringContent(model.Notes), "Notes");
        }

        if (model.Files != null && model.Files.Count > 0)
        {
            foreach (var file in model.Files)
            {
                var streamContent = new StreamContent(file.OpenReadStream());
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                content.Add(streamContent, "Files", file.FileName);
            }
        }

        var response = await client.PostAsync("api/intake/create", content);

        if (response.IsSuccessStatusCode)
        {
            return RedirectToAction("Index");
        }

        ModelState.AddModelError(string.Empty, "Failed to create intake.");
        return View(model);
    }


    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
