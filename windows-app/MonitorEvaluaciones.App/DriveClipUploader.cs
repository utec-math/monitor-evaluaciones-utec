using System.Net.Http.Json;
using System.Text.Json;

namespace MonitorEvaluaciones.App;

public sealed class DriveClipUploader
{
    private const string EndpointConfigUrl = "https://raw.githubusercontent.com/utec-math/monitor-evaluaciones-utec/main/drive-receiver/endpoint.txt";
    private readonly HttpClient http;
    private readonly FirebaseAnonymousAuth auth;

    public DriveClipUploader(HttpClient httpClient, FirebaseAnonymousAuth firebaseAuth)
    {
        http = httpClient;
        auth = firebaseAuth;
    }

    public async Task<ClipUploadResult> UploadAsync(string session, string studentId, ClipResult clip)
    {
        var receiverUrl = await ResolveReceiverUrlAsync();
        if (string.IsNullOrWhiteSpace(receiverUrl))
            return new ClipUploadResult(false, "", "", "El receptor de Drive todavía no está desplegado.");
        if (!Uri.TryCreate(receiverUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("https" or "http"))
            return new ClipUploadResult(false, "", "", "La URL del receptor no es válida.");
        if (!File.Exists(clip.FilePath))
            return new ClipUploadResult(false, "", "", "No se encontró el clip local.");
        if (!await auth.EnsureSignedInAsync())
            return new ClipUploadResult(false, "", "", "No se pudo autenticar la app en Firebase.");

        var bytes = await File.ReadAllBytesAsync(clip.FilePath);
        var payload = new
        {
            idToken = auth.IdToken,
            clientUid = auth.LocalId,
            session,
            studentId,
            fileName = Path.GetFileName(clip.FilePath),
            contentType = "video/x-msvideo",
            dataBase64 = Convert.ToBase64String(bytes),
            triggeredAt = clip.TriggeredAt.ToUnixTimeMilliseconds(),
            reason = clip.Reason,
            detail = clip.Detail
        };

        using var response = await http.PostAsJsonAsync(endpoint, payload);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return new ClipUploadResult(false, "", "", $"El receptor respondió {response.StatusCode}: {Short(text)}");

        var result = JsonSerializer.Deserialize<ReceiverResponse>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result?.Ok != true || string.IsNullOrWhiteSpace(result.WebViewLink))
            return new ClipUploadResult(false, result?.FileId ?? "", result?.WebViewLink ?? "", result?.Error ?? "El receptor no devolvió un enlace de Drive.");

        return new ClipUploadResult(true, result.FileId ?? "", result.WebViewLink, "");
    }

    private async Task<string> ResolveReceiverUrlAsync()
    {
        try
        {
            var text = (await http.GetStringAsync(EndpointConfigUrl)).Trim();
            return text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? text : "";
        }
        catch { return ""; }
    }

    private static string Short(string text)
    {
        text = (text ?? "").Trim();
        return text.Length <= 300 ? text : text[..300];
    }

    private sealed class ReceiverResponse
    {
        public bool Ok { get; set; }
        public string? FileId { get; set; }
        public string? WebViewLink { get; set; }
        public string? Error { get; set; }
    }
}

public sealed record ClipUploadResult(bool Ok, string FileId, string WebViewLink, string Error);
