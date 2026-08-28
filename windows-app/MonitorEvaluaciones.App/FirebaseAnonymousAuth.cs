using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;

namespace MonitorEvaluaciones.App;

public sealed class FirebaseAnonymousAuth
{
    private const string BridgeUrl = "https://utec-math.github.io/monitor-evaluaciones-utec/auth-bridge.html";

    private readonly WebView2 bridge = new() { Dock = DockStyle.Fill };
    private Form? host;
    private bool initialized;
    private TaskCompletionSource<bool>? pending;
    private DateTimeOffset expiresAt = DateTimeOffset.MinValue;

    public string IdToken { get; private set; } = "";
    public string LocalId { get; private set; } = "";

    public FirebaseAnonymousAuth(HttpClient _)
    {
    }

    public async Task<bool> EnsureSignedInAsync()
    {
        if (!string.IsNullOrWhiteSpace(IdToken) && DateTimeOffset.UtcNow < expiresAt.AddMinutes(-3))
            return true;

        try
        {
            await EnsureBridgeAsync();
            pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bridge.CoreWebView2.Navigate(BridgeUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            var completed = await Task.WhenAny(pending.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            if (completed != pending.Task)
                return false;
            return await pending.Task;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureBridgeAsync()
    {
        if (initialized) return;

        host = new Form
        {
            Width = 2,
            Height = 2,
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Opacity = 0
        };
        host.Controls.Add(bridge);
        host.Show();

        await bridge.EnsureCoreWebView2Async();
        bridge.CoreWebView2.Settings.AreDevToolsEnabled = false;
        bridge.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        bridge.CoreWebView2.WebMessageReceived += (_, e) => ReceiveMessage(e.WebMessageAsJson);
        initialized = true;
    }

    private void ReceiveMessage(string json)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<AuthBridgeMessage>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (msg is null || !string.Equals(msg.Type, "firebase-auth", StringComparison.OrdinalIgnoreCase))
                return;

            if (msg.Ok && !string.IsNullOrWhiteSpace(msg.IdToken) && !string.IsNullOrWhiteSpace(msg.LocalId))
            {
                IdToken = msg.IdToken;
                LocalId = msg.LocalId;
                expiresAt = msg.ExpiresAt > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(msg.ExpiresAt)
                    : DateTimeOffset.UtcNow.AddMinutes(50);
                pending?.TrySetResult(true);
            }
            else
            {
                pending?.TrySetResult(false);
            }
        }
        catch
        {
            pending?.TrySetResult(false);
        }
    }

    private sealed class AuthBridgeMessage
    {
        public string Type { get; set; } = "";
        public bool Ok { get; set; }
        public string IdToken { get; set; } = "";
        public string LocalId { get; set; } = "";
        public long ExpiresAt { get; set; }
        public string Error { get; set; } = "";
    }
}
