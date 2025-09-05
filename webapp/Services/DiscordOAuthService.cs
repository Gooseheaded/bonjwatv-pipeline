using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace bwkt_webapp.Services;

public class DiscordOAuthService
{
    private readonly HttpClient _http;
    public DiscordOAuthService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public async Task<(bool ok, string? accessToken, string? error)> ExchangeCodeAsync(string code, string redirectUri)
    {
        var mock = Environment.GetEnvironmentVariable("DISCORD_OAUTH_MOCK");
        if (string.Equals(mock, "true", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "mock-access-token", null);
        }

        var clientId = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return (false, null, "Missing Discord OAuth credentials");
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId!,
            ["client_secret"] = clientSecret!,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        };
        using var content = new FormUrlEncodedContent(form);
        var resp = await _http.PostAsync("https://discord.com/api/oauth2/token", content);
        if (!resp.IsSuccessStatusCode)
        {
            return (false, null, $"Token exchange failed: {(int)resp.StatusCode}");
        }
        var json = await resp.Content.ReadAsStringAsync();
        var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOptions()) ?? new();
        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return (false, null, "No access token returned");
        }
        return (true, token.AccessToken, null);
    }

    public async Task<(bool ok, DiscordUser? user, string? error)> GetUserAsync(string accessToken)
    {
        var mock = Environment.GetEnvironmentVariable("DISCORD_OAUTH_MOCK");
        if (string.Equals(mock, "true", StringComparison.OrdinalIgnoreCase))
        {
            return (true, new DiscordUser { Id = Guid.NewGuid().ToString(), Username = "DiscordUser", Avatar = null }, null);
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            return (false, null, $"User fetch failed: {(int)resp.StatusCode}");
        }
        var json = await resp.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<DiscordUser>(json, JsonOptions());
        return user is null ? (false, null, "Invalid user payload") : (true, user, null);
    }

    private static JsonSerializerOptions JsonOptions() => new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }

    public class DiscordUser
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("global_name")] public string? GlobalName { get; set; }
        [JsonPropertyName("avatar")] public string? Avatar { get; set; }
        [JsonPropertyName("discriminator")] public string? Discriminator { get; set; }
    }
}

