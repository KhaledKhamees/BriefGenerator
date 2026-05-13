using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using BriefGenerator.Web.Models;
using BriefGenerator.Web.Services;

namespace BriefGenerator.Web.Controllers;

public class AccountController : Controller
{
    private readonly IHttpClientFactory _clientFactory;

    public AccountController(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    // ── GET /Account/Login ────────────────────────────────────────────────
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    // ── POST /Account/Login ───────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
            return View(model);

        var client = _clientFactory.CreateClient("ApiClient");
        var payload = new { email = model.Email, password = model.Password };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/auth/login", content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty,
                TryParseError(body) ?? "Invalid email or password.");
            return View(model);
        }

        var data      = JsonSerializer.Deserialize<JsonElement>(body);
        var token     = data.GetProperty("token").GetString()!;
        var userName  = data.GetProperty("user").GetProperty("name").GetString()!;
        var userEmail = data.GetProperty("user").GetProperty("email").GetString()!;
        var userId    = data.GetProperty("user").GetProperty("id").GetString()!;

        await SignInAsync(token, userId, userName, userEmail, model.RememberMe);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    // ── GET /Account/Register ─────────────────────────────────────────────
    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View();
    }

    // ── POST /Account/Register ────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var client = _clientFactory.CreateClient("ApiClient");
        var payload = new { name = model.Name, email = model.Email, password = model.Password };
        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/auth/register", content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError(string.Empty,
                TryParseError(body) ?? "Registration failed. Please try again.");
            return View(model);
        }

        var data      = JsonSerializer.Deserialize<JsonElement>(body);
        var token     = data.GetProperty("token").GetString()!;
        var userName  = data.GetProperty("user").GetProperty("name").GetString()!;
        var userEmail = data.GetProperty("user").GetProperty("email").GetString()!;
        var userId    = data.GetProperty("user").GetProperty("id").GetString()!;

        await SignInAsync(token, userId, userName, userEmail, isPersistent: true);

        TempData["Success"] = $"Welcome, {userName}! Your account has been created.";
        return RedirectToAction("Index", "Home");
    }

    // ── POST /Account/Logout ──────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    // ── Shared sign-in helper ─────────────────────────────────────────────
    private async Task SignInAsync(
        string token, string userId, string userName, string userEmail, bool isPersistent)
    {
        // Store JWT in server-side session — never leaves the server
        HttpContext.Session.SetString(AuthenticatedApiClient.SessionKey, token);

        // Auth cookie carries only small identity claims (no JWT)
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name,           userName),
            new(ClaimTypes.Email,          userEmail),
        };

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                ExpiresUtc   = DateTimeOffset.UtcNow.AddDays(7)
            });
    }

    private static string? TryParseError(string body)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(body);
            if (doc.TryGetProperty("error", out var err)) return err.GetString();
        }
        catch { }
        return null;
    }
}
