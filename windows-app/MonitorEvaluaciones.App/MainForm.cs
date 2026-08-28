using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MonitorEvaluaciones.App;

public sealed class MainForm : Form
{
    private const string FirebaseBase = "https://preciencia1-default-rtdb.firebaseio.com";
    private const string DefaultHome = "https://utec-math.github.io/monitor-evaluaciones-utec/";

    private readonly TextBox sessionBox = new() { Width = 180 };
    private readonly TextBox studentBox = new() { Width = 150 };
    private readonly Button connectButton = new() { Text = "Conectar", AutoSize = true };
    private readonly Button homeButton = new() { Text = "Inicio", AutoSize = true };
    private readonly Label statusLabel = new() { AutoSize = true, Text = "Sin conectar", Padding = new Padding(8, 7, 0, 0) };
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer syncTimer = new() { Interval = 2500 };
    private readonly HttpClient http = new();

    private string session = "";
    private string studentId = "";
    private SessionConfig config = new();
    private string lastCommandId = "";
    private DateTimeOffset? unlockedUntil;
    private DateTimeOffset lastHeartbeat = DateTimeOffset.MinValue;
    private bool finished;
    private bool syncBusy;

    public MainForm(string? initialSession, string? initialStudent = null)
    {
        Text = "Monitor Evaluaciones UTEC · Modo examen";
        Width = 1180;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        if (!string.IsNullOrWhiteSpace(initialSession))
            sessionBox.Text = CleanKey(initialSession, 60);
        if (!string.IsNullOrWhiteSpace(initialStudent))
            studentBox.Text = CleanKey(initialStudent, 80);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(10, 8, 10, 6),
            WrapContents = false,
            AutoSize = false
        };
        top.Controls.Add(new Label { Text = "Sesión:", AutoSize = true, Padding = new Padding(0, 7, 3, 0) });
        top.Controls.Add(sessionBox);
        top.Controls.Add(new Label { Text = "ID / cédula:", AutoSize = true, Padding = new Padding(8, 7, 3, 0) });
        top.Controls.Add(studentBox);
        top.Controls.Add(connectButton);
        top.Controls.Add(homeButton);
        top.Controls.Add(statusLabel);

        Controls.Add(browser);
        Controls.Add(top);

        connectButton.Click += async (_, _) => await ConnectAsync();
        homeButton.Click += (_, _) => NavigateHome();
        syncTimer.Tick += async (_, _) => await SyncAsync();
        FormClosing += (_, _) =>
        {
            syncTimer.Stop();
            _ = SendPresenceAsync(false);
        };

        Shown += async (_, _) =>
        {
            await browser.EnsureCoreWebView2Async();
            ConfigureBrowser();
            if (!string.IsNullOrWhiteSpace(sessionBox.Text) && !string.IsNullOrWhiteSpace(studentBox.Text))
                await ConnectAsync();
            else
                ShowMessage("Ingresá el código de sesión y tu documento/ID, y presioná Conectar.");
        };
    }

    private void ConfigureBrowser()
    {
        browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        browser.CoreWebView2.Settings.IsStatusBarEnabled = false;

        browser.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase)) return;
            if (IsAllowed(e.Uri)) return;
            e.Cancel = true;
            ShowBlocked(e.Uri);
        };

        browser.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (IsAllowed(e.Uri))
                browser.CoreWebView2.Navigate(e.Uri);
            else
                ShowBlocked(e.Uri);
        };
    }

    private async Task ConnectAsync()
    {
        session = CleanKey(sessionBox.Text, 60);
        studentId = CleanKey(studentBox.Text, 80);
        sessionBox.Text = session;
        studentBox.Text = studentId;

        if (string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(studentId))
        {
            MessageBox.Show("Ingresá el código de sesión y tu documento/ID.", "Monitor UTEC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        statusLabel.Text = "Conectando…";
        lastCommandId = "";
        finished = false;
        unlockedUntil = null;

        if (await RefreshConfigAsync(true))
        {
            await SendPresenceAsync(true);
            syncTimer.Start();
            UpdateStatus();
            NavigateHome();
        }
    }

    private async Task SyncAsync()
    {
        if (syncBusy || string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(studentId)) return;
        syncBusy = true;
        try
        {
            await RefreshConfigAsync(false);
            await CheckCommandAsync();
            if (DateTimeOffset.UtcNow - lastHeartbeat >= TimeSpan.FromSeconds(5))
                await SendPresenceAsync(true);
            UpdateStatus();
        }
        finally
        {
            syncBusy = false;
        }
    }

    private async Task<bool> RefreshConfigAsync(bool showErrors)
    {
        if (string.IsNullOrWhiteSpace(session)) return false;

        try
        {
            var url = $"{FirebaseBase}/sessions/{Uri.EscapeDataString(session)}/config.json";
            using var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var loaded = JsonSerializer.Deserialize<SessionConfig>(json, JsonOptions()) ?? new SessionConfig();
            loaded.Normalize();
            config = loaded;

            if (!IsUnlocked && !finished && browser.Source is Uri current && current.Scheme is "http" or "https" && !IsAllowed(current.ToString()))
                ShowBlocked(current.ToString());
            return true;
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Sin acceso a configuración";
            if (showErrors)
                MessageBox.Show("No se pudo leer la configuración de la sesión.\n\n" + ex.Message,
                    "Monitor UTEC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private async Task CheckCommandAsync()
    {
        try
        {
            var url = $"{FirebaseBase}/sessions/{Uri.EscapeDataString(session)}/commands/{Uri.EscapeDataString(studentId)}.json";
            using var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var command = JsonSerializer.Deserialize<RemoteCommand>(json, JsonOptions());
            if (command is null || string.IsNullOrWhiteSpace(command.Id) || command.Id == lastCommandId) return;

            lastCommandId = command.Id;
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (command.ExpiresAt > 0 && command.ExpiresAt < nowMs) return;
            await ExecuteCommandAsync(command);
        }
        catch
        {
        }
    }

    private async Task ExecuteCommandAsync(RemoteCommand command)
    {
        switch ((command.Action ?? "").Trim().ToLowerInvariant())
        {
            case "home":
                finished = false;
                NavigateHome();
                break;
            case "reload":
                if (!finished && browser.CoreWebView2 is not null)
                    browser.Reload();
                break;
            case "recover":
                finished = false;
                unlockedUntil = null;
                await RefreshConfigAsync(false);
                NavigateHome();
                break;
            case "unlock":
                finished = false;
                var seconds = Math.Clamp(command.DurationSec <= 0 ? 120 : command.DurationSec, 15, 1800);
                unlockedUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
                MessageBox.Show($"El docente habilitó navegación libre durante {Math.Ceiling(seconds / 60.0):0.#} min.",
                    "Monitor UTEC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            case "lock":
                unlockedUntil = null;
                if (browser.Source is Uri current && current.Scheme is "http" or "https" && !IsAllowed(current.ToString()))
                    NavigateHome();
                break;
            case "finish":
                finished = true;
                unlockedUntil = null;
                ShowMessage("<h2>Evaluación finalizada</h2><p>El docente finalizó esta sesión en la aplicación de examen.</p><p>Mantené esta ventana abierta hasta recibir indicaciones.</p>", true);
                break;
        }

        await SendPresenceAsync(true);
        UpdateStatus();
    }

    private async Task SendPresenceAsync(bool connected)
    {
        if (string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(studentId)) return;
        try
        {
            var url = $"{FirebaseBase}/sessions/{Uri.EscapeDataString(session)}/clients/{Uri.EscapeDataString(studentId)}.json";
            var payload = new
            {
                id = studentId,
                app = "windows-webview2",
                version = "0.2",
                lastSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                connected,
                state = finished ? "finished" : IsUnlocked ? "unlocked" : "locked",
                currentUrl = browser.Source?.ToString() ?? ""
            };
            using var response = await http.PutAsJsonAsync(url, payload);
            if (response.IsSuccessStatusCode)
                lastHeartbeat = DateTimeOffset.UtcNow;
        }
        catch
        {
        }
    }

    private bool IsUnlocked => unlockedUntil.HasValue && unlockedUntil.Value > DateTimeOffset.UtcNow;

    private void UpdateStatus()
    {
        if (string.IsNullOrWhiteSpace(session))
        {
            statusLabel.Text = "Sin conectar";
            return;
        }
        if (finished)
            statusLabel.Text = $"Finalizada · {session}";
        else if (IsUnlocked)
        {
            var left = unlockedUntil!.Value - DateTimeOffset.UtcNow;
            statusLabel.Text = $"🔓 Desbloqueado · {Math.Max(1, Math.Ceiling(left.TotalSeconds))} s";
        }
        else
            statusLabel.Text = $"🔒 Conectado · {session} · {config.AllowedSites.Count} recurso(s) extra";
    }

    private void NavigateHome()
    {
        if (finished) return;
        var target = string.IsNullOrWhiteSpace(config.HomeUrl) ? DefaultHome : config.HomeUrl;
        if (browser.CoreWebView2 is not null)
            browser.CoreWebView2.Navigate(target);
    }

    private bool IsAllowed(string? raw)
    {
        if (finished) return false;
        if (IsUnlocked) return Uri.TryCreate(raw, UriKind.Absolute, out var free) && free.Scheme is "http" or "https";
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var target)) return false;
        if (target.Scheme is not ("http" or "https")) return false;

        if (MatchesExactOrConfiguredHome(target, config.HomeUrl)) return true;

        foreach (var site in config.AllowedSites)
        {
            if (!Uri.TryCreate(site.Url, UriKind.Absolute, out var allowed)) continue;

            if (site.Scope == "domain")
            {
                if (string.Equals(target.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (site.Scope == "path")
            {
                if (!string.Equals(target.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)) continue;
                var basePath = allowed.AbsolutePath.TrimEnd('/') + "/";
                var targetPath = target.AbsolutePath.TrimEnd('/') + "/";
                if (targetPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (UrisEquivalent(target, allowed))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesExactOrConfiguredHome(Uri target, string? home)
    {
        var effective = string.IsNullOrWhiteSpace(home) ? DefaultHome : home;
        return Uri.TryCreate(effective, UriKind.Absolute, out var h) && UrisEquivalent(target, h);
    }

    private static bool UrisEquivalent(Uri a, Uri b)
    {
        var left = a.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.Unescaped).TrimEnd('/');
        var right = b.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.Unescaped).TrimEnd('/');
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private void ShowBlocked(string? attempted)
    {
        var safe = System.Net.WebUtility.HtmlEncode(attempted ?? "dirección desconocida");
        ShowMessage($"<h2>Navegación no habilitada</h2><p>Esta dirección no está permitida para la evaluación:</p><p><code>{safe}</code></p><p>Si necesitás acceder, solicitá al docente que la habilite.</p>", true);
    }

    private void ShowMessage(string message, bool html = false)
    {
        if (browser.CoreWebView2 is null) return;
        var body = html ? message : $"<p>{System.Net.WebUtility.HtmlEncode(message)}</p>";
        var page = "<!doctype html><html lang=\"es\"><meta charset=\"utf-8\"><style>" +
                   "body{font-family:Segoe UI,Arial,sans-serif;background:#eef3f5;color:#17333d;padding:48px}" +
                   "main{max-width:720px;margin:auto;background:white;border:1px solid #dce6e9;border-radius:14px;padding:28px}" +
                   "code{word-break:break-all;background:#f3f6f7;padding:4px 6px;border-radius:5px}" +
                   "</style><main>" + body + "</main></html>";
        browser.NavigateToString(page);
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };

    private static string CleanKey(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return new string(value.Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(max).ToArray());
    }
}

public sealed class SessionConfig
{
    public string HomeUrl { get; set; } = DefaultHomeValue;
    public List<AllowedSite> AllowedSites { get; set; } = new();
    private const string DefaultHomeValue = "https://utec-math.github.io/monitor-evaluaciones-utec/";

    public void Normalize()
    {
        if (!Uri.TryCreate(HomeUrl, UriKind.Absolute, out var h) || h.Scheme is not ("http" or "https"))
            HomeUrl = DefaultHomeValue;
        AllowedSites ??= new();
        AllowedSites = AllowedSites
            .Where(x => x is not null && Uri.TryCreate(x.Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            .Take(30)
            .ToList();
        foreach (var x in AllowedSites)
            if (x.Scope is not ("exact" or "path" or "domain")) x.Scope = "exact";
    }
}

public sealed class AllowedSite
{
    public string Url { get; set; } = "";
    public string Scope { get; set; } = "exact";
}

public sealed class RemoteCommand
{
    public string Id { get; set; } = "";
    public string Action { get; set; } = "";
    public long IssuedAt { get; set; }
    public long ExpiresAt { get; set; }
    public int DurationSec { get; set; }
}
