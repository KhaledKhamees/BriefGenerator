using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF community licence (free for open-source / small projects)
QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddControllersWithViews();

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
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Public brief route — no auth required
app.MapControllerRoute(
    name: "publicBrief",
    pattern: "brief/{token}",
    defaults: new { controller = "PublicBrief", action = "ClientBriefView" });

app.Run();
