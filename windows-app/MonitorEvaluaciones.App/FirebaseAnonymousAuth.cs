using System.Net.Http.Json;
using System.Text.Json;

namespace MonitorEvaluaciones.App;

public sealed class FirebaseAnonymousAuth
{
    private const string ApiKey = "AIzaSyD2vgqvLLwcJYPc0gca2pC_ud0q31sxkXY";
    private readonly HttpClient http;
    private string refreshToken = "";
    private DateTimeOffset expiresAt = DateTimeOffset.MinValue;

    public string IdToken { get; private set; } = "";
    public string LocalId { get; private set; } = "";

    public FirebaseAnonymousAuth(HttpClient httpClient) => http = httpClient;

    public async Task<bool> EnsureSignedInAsync()
    {
        if (!string.IsNullOrWhiteSpace(IdToken) && DateTimeOffset.UtcNow < expiresAt.AddMinutes(-5)) return true;
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            if (await RefreshAsync()) return true;
        }
        return await SignInAsync();
    }

    private async Task<bool> SignInAsync()
    {
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={ApiKey}";
        using var response = await http.PostAsJsonAsync(url, new { returnSecureToken = true });
        if (!response.IsSuccessStatusCode) return false;
        var json = await response.Content.ReadAsStringAsync();
        var obj = JsonSerializer.Deserialize<AuthResponse>(json, JsonOptions());
        if (obj is null || string.IsNullOrWhiteSpace(obj.IdToken) || string.IsNullOrWhiteSpace(obj.LocalId)) return false;
        Apply(obj.IdToken, obj.LocalId, obj.RefreshToken, obj.ExpiresIn);
        return true;
    }

    private async Task<bool> RefreshAsync()
    {
        var url = $"https://securetoken.googleapis.com/v1/token?key={ApiKey}";
        using var content = new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });
        using var response = await http.PostAsync(url, content);
        if (!response.IsSuccessStatusCode) return false;
        var json = await response.Content.ReadAsStringAsync();
        var obj = JsonSerializer.Deserialize<RefreshResponse>(json, JsonOptions());
        if (obj is null || string.IsNullOrWhiteSpace(obj.IdToken) || string.IsNullOrWhiteSpace(obj.UserId)) return false;
        Apply(obj.IdToken, obj.UserId, obj.RefreshToken, obj.ExpiresIn);
        return true;
    }

    private void Apply(string token, string localId, string refresh, string expires)
    {
        IdToken = token;
        LocalId = localId;
        refreshToken = refresh;
        _ = int.TryParse(expires, out var seconds);
        expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds > 0 ? seconds : 3600);
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private sealed class AuthResponse
    {
        public string IdToken { get; set; } = "";
        public string LocalId { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string ExpiresIn { get; set; } = "3600";
    }

    private sealed class RefreshResponse
    {
        public string IdToken { get; set; } = "";
        public string UserId { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string ExpiresIn { get; set; } = "3600";
    }
}
