using System.Net.Http.Headers;

namespace BriefGenerator.Web.Services;

/// <summary>
/// Wraps the named HttpClient and attaches the JWT on every API call.
/// The JWT is stored in the server-side session under the key "JwtToken".
/// </summary>
public class AuthenticatedApiClient
{
    private readonly IHttpClientFactory _factory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public const string SessionKey = "JwtToken";

    public AuthenticatedApiClient(IHttpClientFactory factory, IHttpContextAccessor httpContextAccessor)
    {
        _factory = factory;
        _httpContextAccessor = httpContextAccessor;
    }

    public HttpClient CreateClient()
    {
        var client = _factory.CreateClient("ApiClient");
        var token = GetToken();

        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    public string? GetToken() =>
        _httpContextAccessor.HttpContext?.Session.GetString(SessionKey);
}
