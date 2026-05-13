using BriefGenerator.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// ── Cookie authentication (identity only — no JWT in here) ────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Account/Login";
        options.LogoutPath       = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan   = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly  = true;
        options.Cookie.SameSite  = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// ── Session — stores the JWT server-side, only a session ID goes to browser ──
builder.Services.AddDistributedMemoryCache();   // required backing store for session
builder.Services.AddSession(options =>
{
    options.IdleTimeout              = TimeSpan.FromDays(7);
    options.Cookie.HttpOnly          = true;
    options.Cookie.IsEssential       = true;
    options.Cookie.SameSite          = SameSiteMode.Lax;
    options.Cookie.SecurePolicy      = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticatedApiClient>();
builder.Services.AddHttpClient("ApiClient", client =>
{
    var baseUrl = builder.Configuration["ApiBaseUrl"];
    client.BaseAddress = new Uri(
        string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:5018/" : baseUrl);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Order matters: Session → Authentication → Authorization
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Public brief route — no auth required
app.MapControllerRoute(
    name: "publicBrief",
    pattern: "brief/{token}",
    defaults: new { controller = "PublicBrief", action = "ClientBriefView" });

app.Run();
