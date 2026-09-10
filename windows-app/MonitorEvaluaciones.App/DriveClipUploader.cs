using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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

        // El receptor actual espera JSON con dataBase64. Antes se leía el AVI entero
        // en memoria y luego se construía otro string Base64 también completo.
        // Este contenido conserva exactamente el mismo formato JSON, pero codifica
        // el archivo por streaming mientras HttpClient lo envía.
        var metadata = new UploadMetadata(
            auth.IdToken,
            auth.LocalId,
            session,
            studentId,
            Path.GetFileName(clip.FilePath),
            "video/x-msvideo",
            clip.TriggeredAt.ToUnixTimeMilliseconds(),
            clip.Reason,
            clip.Detail);

        string lastError = "No se pudo subir el clip.";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var content = new StreamingBase64JsonContent(clip.FilePath, metadata);
                using var response = await http.PostAsync(endpoint, content);
                var text = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"El receptor respondió {response.StatusCode}: {Short(text)}";
                    if ((int)response.StatusCode < 500) break;
                }
                else
                {
                    var result = JsonSerializer.Deserialize<ReceiverResponse>(text,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (result?.Ok == true && !string.IsNullOrWhiteSpace(result.WebViewLink))
                        return new ClipUploadResult(true, result.FileId ?? "", result.WebViewLink, "");

                    lastError = result?.Error ?? "El receptor no devolvió un enlace de Drive.";
                    break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                lastError = ex.Message;
            }

            if (attempt < 3)
                await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 2 : 5));
        }

        return new ClipUploadResult(false, "", "", lastError);
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

    private sealed record UploadMetadata(
        string IdToken,
        string ClientUid,
        string Session,
        string StudentId,
        string FileName,
        string ContentType,
        long TriggeredAt,
        string Reason,
        string Detail);

    private sealed class StreamingBase64JsonContent : HttpContent
    {
        private readonly string filePath;
        private readonly byte[] prefix;
        private readonly byte[] suffix = Encoding.UTF8.GetBytes("\"}");
        private readonly long contentLength;

        public StreamingBase64JsonContent(string path, UploadMetadata metadata)
        {
            filePath = path;
            var prefixText = "{" +
                "\"idToken\":" + Json(metadata.IdToken) + "," +
                "\"clientUid\":" + Json(metadata.ClientUid) + "," +
                "\"session\":" + Json(metadata.Session) + "," +
                "\"studentId\":" + Json(metadata.StudentId) + "," +
                "\"fileName\":" + Json(metadata.FileName) + "," +
                "\"contentType\":" + Json(metadata.ContentType) + "," +
                "\"triggeredAt\":" + metadata.TriggeredAt + "," +
                "\"reason\":" + Json(metadata.Reason) + "," +
                "\"detail\":" + Json(metadata.Detail) + "," +
                "\"dataBase64\":\"";

            prefix = Encoding.UTF8.GetBytes(prefixText);
            var fileLength = new FileInfo(filePath).Length;
            var base64Length = 4L * ((fileLength + 2L) / 3L);
            contentLength = prefix.LongLength + base64Length + suffix.LongLength;
            Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(prefix);
            await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);

            using (var transform = new ToBase64Transform())
            using (var base64Stream = new CryptoStream(stream, transform, CryptoStreamMode.Write, leaveOpen: true))
            {
                await file.CopyToAsync(base64Stream, 64 * 1024);
                base64Stream.FlushFinalBlock();
            }

            await stream.WriteAsync(suffix);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = contentLength;
            return true;
        }

        private static string Json(string? value) => JsonSerializer.Serialize(value ?? "");
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
