using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MonitorEvaluaciones.App;

public sealed class MainForm : Form
{
    private const string FirebaseBase = "https://preciencia1-default-rtdb.firebaseio.com";
    private const string DefaultHome = "https://utec-math.github.io/monitor-evaluaciones-utec/";
    private const string StudentPage = "https://utec-math.github.io/monitor-evaluaciones-utec/estudiante-v08.html";

    private readonly TextBox sessionBox = new() { Width = 165 };
    private readonly TextBox nameBox = new() { Width = 190 };
    private readonly TextBox studentBox = new() { Width = 135 };
    private readonly Button connectButton = new() { Text = "Entrar a la evaluación", AutoSize = true };
    private readonly Button homeButton = new() { Text = "Inicio", AutoSize = true };
    private readonly Label statusLabel = new() { AutoSize = true, Text = "● DESCONECTADO", Padding = new Padding(8, 7, 8, 0), Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.Firebrick };
    private readonly Label identityLabel = new() { AutoSize = true, Padding = new Padding(8, 8, 8, 0), ForeColor = Color.FromArgb(55, 78, 86) };
    private readonly FlowLayoutPanel entryBar = new() { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 8, 10, 6), WrapContents = false, AutoSize = false };
    private readonly FlowLayoutPanel connectedBar = new() { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 8, 10, 6), WrapContents = false, AutoSize = false, Visible = false, BackColor = Color.FromArgb(239, 247, 247) };
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer syncTimer = new() { Interval = 2500 };
    private readonly HttpClient http = new();
    private readonly ScreenEventRecorder recorder = new();
    private readonly FirebaseAnonymousAuth firebaseAuth;
    private readonly DriveClipUploader clipUploader;

    private string session = "";
    private string studentId = "";
    private string studentName = "";
    private SessionConfig config = new();
    private string lastCommandId = "";
    private DateTimeOffset? unlockedUntil;
    private DateTimeOffset lastHeartbeat = DateTimeOffset.MinValue;
    private DateTimeOffset lastFocusEvent = DateTimeOffset.MinValue;
    private bool finished;
    private bool syncBusy;
    private bool connectedOnce;
    private bool closing;
    private bool presenceOk;

    public MainForm(string? initialSession, string? initialStudent = null)
    {
        firebaseAuth = new FirebaseAnonymousAuth(http);
        clipUploader = new DriveClipUploader(http, firebaseAuth);

        Text = "Monitor Evaluaciones UTEC · v0.8";
        Width = 1240;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;

        if (!string.IsNullOrWhiteSpace(initialSession))
            sessionBox.Text = CleanKey(initialSession, 60);
        if (!string.IsNullOrWhiteSpace(initialStudent))
            studentBox.Text = CleanKey(initialStudent, 80);

        entryBar.Controls.Add(new Label { Text = "Sesión:", AutoSize = true, Padding = new Padding(0, 7, 3, 0) });
        entryBar.Controls.Add(sessionBox);
        entryBar.Controls.Add(new Label { Text = "Nombre:", AutoSize = true, Padding = new Padding(8, 7, 3, 0) });
        entryBar.Controls.Add(nameBox);
        entryBar.Controls.Add(new Label { Text = "Documento:", AutoSize = true, Padding = new Padding(8, 7, 3, 0) });
        entryBar.Controls.Add(studentBox);
        entryBar.Controls.Add(connectButton);

        connectedBar.Controls.Add(statusLabel);
        connectedBar.Controls.Add(identityLabel);
        connectedBar.Controls.Add(homeButton);

        Controls.Add(browser);
        Controls.Add(connectedBar);
        Controls.Add(entryBar);

        recorder.ClipSaved += result => BeginInvoke(async () => await OnClipSavedAsync(result));
        recorder.RecorderError += error => BeginInvoke(async () => await SendAppEventAsync("captura_error", "orange", error));

        connectButton.Click += async (_, _) => await ConnectAsync();
        homeButton.Click += (_, _) => NavigateHome();
        syncTimer.Tick += async (_, _) => await SyncAsync();
        Deactivate += async (_, _) => await OnAppDeactivatedAsync();

        FormClosing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true;
            closing = true;
            syncTimer.Stop();
            await SendPresenceAsync(false);
            await recorder.StopAsync();
            e.Cancel = false;
            Close();
        };

        Shown += async (_, _) =>
        {
            await browser.EnsureCoreWebView2Async();
            ConfigureBrowser();
            ShowMessage("Completá sesión, nombre y documento. Después presioná Entrar a la evaluación.");
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
        studentName = (nameBox.Text ?? "").Trim();
        sessionBox.Text = session;
        studentBox.Text = studentId;
        nameBox.Text = studentName;

        if (string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(studentName))
        {
            MessageBox.Show("Completá sesión, nombre y documento.", "Monitor UTEC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!connectedOnce)
        {
            var consent = MessageBox.Show(
                "Durante esta evaluación la aplicación mantiene un búfer temporal de la pantalla y conserva únicamente clips alrededor de eventos relevantes (por ejemplo, cambiar a otra aplicación).\n\n" +
                "Configuración: 30 s antes + 30 s después, 2 imágenes por segundo, sin audio. Los clips se guardan para revisión humana y, cuando el receptor institucional está disponible, se suben automáticamente a Drive. Ningún evento implica una sanción automática.\n\n" +
                "¿Continuar e ingresar a la evaluación?",
                "Información de supervisión",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (consent != DialogResult.OK) return;
        }

        connectButton.Enabled = false;
        sessionBox.ReadOnly = true;
        nameBox.ReadOnly = true;
        studentBox.ReadOnly = true;
        statusLabel.Text = "Autenticando…";
        if (!await firebaseAuth.EnsureSignedInAsync())
        {
            connectButton.Enabled = true;
            sessionBox.ReadOnly = false;
            nameBox.ReadOnly = false;
            studentBox.ReadOnly = false;
            MessageBox.Show(
                "No se pudo autenticar esta instalación. Verificá la conexión a Internet e intentá nuevamente.",
                "Monitor UTEC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            statusLabel.Text = "Autenticación no disponible";
            return;
        }

        statusLabel.Text = "Conectando…";
        lastCommandId = "";
        finished = false;
        unlockedUntil = null;

        if (await RefreshConfigAsync(true))
        {
            recorder.Start(session, studentId);
            connectedOnce = true;
            await SendPresenceAsync(true);
            syncTimer.Start();
            identityLabel.Text = $"{studentName}  ·  Documento {studentId}  ·  Sesión {session}";
            entryBar.Visible = false;
            connectedBar.Visible = true;
            UpdateStatus();
            NavigateHome();
        }
        else
        {
            connectButton.Enabled = true;
            sessionBox.ReadOnly = false;
            nameBox.ReadOnly = false;
            studentBox.ReadOnly = false;
        }
    }

    private async Task OnAppDeactivatedAsync()
    {
        if (!connectedOnce || finished || closing || !recorder.IsRunning) return;
        if (DateTimeOffset.Now - lastFocusEvent < TimeSpan.FromSeconds(8)) return;

        await Task.Delay(180);
        if (ContainsFocus) return;

        var foreground = GetForegroundDescription();
        if (foreground.ProcessId == Environment.ProcessId) return;

        lastFocusEvent = DateTimeOffset.Now;
        var detail = string.IsNullOrWhiteSpace(foreground.Title)
            ? $"La app de examen perdió el foco. Aplicación al frente: {foreground.ProcessName}."
            : $"La app de examen perdió el foco. Aplicación al frente: {foreground.ProcessName} · {foreground.Title}.";

        recorder.Trigger("cambio_aplicacion", detail);
        await SendAppEventAsync("cambio_aplicacion", "yellow", detail);
    }

    private async Task OnClipSavedAsync(ClipResult result)
    {
        var localDetail = $"Clip local asociado a {result.Reason}: {Path.GetFileName(result.FilePath)}";
        await SendAppEventAsync("clip_local_guardado", "info", localDetail);

        var uploaded = await clipUploader.UploadAsync(session, studentId, result);
        if (uploaded.Ok)
        {
            var detail = $"Clip de pantalla disponible para revisión · {result.Reason}";
            await SendAppEventAsync("clip_drive_disponible", "info", detail, uploaded.WebViewLink, uploaded.FileId);
        }
        else
        {
            await SendAppEventAsync("clip_drive_pendiente", "info", uploaded.Error);
        }
    }

    private async Task SendAppEventAsync(string type, string level, string detail, string clipUrl = "", string clipFileId = "")
    {
        if (string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(studentId)) return;
        try
        {
            await firebaseAuth.EnsureSignedInAsync();
            var auth = string.IsNullOrWhiteSpace(firebaseAuth.IdToken) ? "" : "?auth=" + Uri.EscapeDataString(firebaseAuth.IdToken);
            var url = $"{FirebaseBase}/sessions/{Uri.EscapeDataString(session)}/events.json{auth}";
            var payload = new
            {
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                studentId,
                studentName = string.IsNullOrWhiteSpace(studentName) ? studentId : studentName,
                type,
                level,
                detail,
                clipUrl,
                clipFileId
            };
            using var _ = await http.PostAsJsonAsync(url, payload);
        }
        catch { }
    }

    private async Task SyncAsync()
    {
        if (syncBusy || string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(studentId)) return;
        syncBusy = true;
        try
        {
            await firebaseAuth.EnsureSignedInAsync();
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
            if (!await firebaseAuth.EnsureSignedInAsync()) return;
            var url = $"{FirebaseBase}/sessions/{Uri.EscapeDataString(session)}/commands/{Uri.EscapeDataString(studentId)}.json?auth={Uri.EscapeDataString(firebaseAuth.IdToken)}";
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
        catch { }
    }

    private async Task ExecuteCommandAsync(RemoteCommand command)
    {
        switch ((command.Action ?? "").Trim().ToLowerInvariant())
        {
            case "home": finished = false; NavigateHome(); break;
            case "reload": if (!finished && browser.CoreWebView2 is not null) browser.Reload(); break;
            case "recover": finished = false; unlockedUntil = null; await RefreshConfigAsync(false); NavigateHome(); break;
            case "unlock":
                finished = false;
                var seconds = Math.Clamp(command.DurationSec <= 0 ? 120 : command.DurationSec, 15, 1800);
                unlockedUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
                MessageBox.Show($"El docente habilitó navegación libre durante {Math.Ceiling(seconds / 60.0):0.#} min.", "Monitor UTEC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            case "lock":
                unlockedUntil = null;
                if (browser.Source is Uri current && current.Scheme is "http" or "https" && !IsAllowed(current.ToString())) NavigateHome();
                break;
            case "finish":
                finished = true; unlockedUntil = null; await recorder.StopAsync();
                ShowMessage("<h2>Evaluación finalizada</h2><p>El docente finalizó esta sesión.</p><p>Mantené esta ventana abierta hasta recibir indicaciones.</p>", true);
                break;
        }
        await SendPresenceAsync(true); UpdateStatus();
    }

    private async Task SendPresenceAsync(bool connected)
    {
        if (string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(studentId)) return;
        try
        {
            if (!await firebaseAuth.EnsureSignedInAsync()) return;
            var url = $"{FirebaseBase}/sessions/{Uri.EscapeDataString(session)}/clients/{Uri.EscapeDataString(studentId)}.json?auth={Uri.EscapeDataString(firebaseAuth.IdToken)}";
            var payload = new
            {
                id = studentId,
                uid = firebaseAuth.LocalId,
                app = "windows-webview2",
                version = "0.8",
                lastSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                connected,
                state = finished ? "finished" : IsUnlocked ? "unlocked" : "locked",
                currentUrl = browser.Source?.ToString() ?? "",
                eventCapture = recorder.IsRunning
            };
            using var response = await http.PutAsJsonAsync(url, payload);
            presenceOk = response.IsSuccessStatusCode;
            if (presenceOk) lastHeartbeat = DateTimeOffset.UtcNow;
        }
        catch { presenceOk = false; }
    }

    private bool IsUnlocked => unlockedUntil.HasValue && unlockedUntil.Value > DateTimeOffset.UtcNow;

    private void UpdateStatus()
    {
        if (string.IsNullOrWhiteSpace(session))
        {
            statusLabel.Text = "● DESCONECTADO";
            statusLabel.ForeColor = Color.Firebrick;
            return;
        }
        if (finished)
        {
            statusLabel.Text = "● FINALIZADA";
            statusLabel.ForeColor = Color.DimGray;
        }
        else if (IsUnlocked)
        {
            var left = unlockedUntil!.Value - DateTimeOffset.UtcNow;
            statusLabel.Text = $"● CONECTADO · acceso libre {Math.Max(1, Math.Ceiling(left.TotalSeconds))} s";
            statusLabel.ForeColor = Color.DarkGreen;
        }
        else if (!presenceOk || DateTimeOffset.UtcNow - lastHeartbeat > TimeSpan.FromSeconds(12))
        {
            statusLabel.Text = "● RECONECTANDO";
            statusLabel.ForeColor = Color.DarkGoldenrod;
        }
        else
        {
            statusLabel.Text = "● CONECTADO";
            statusLabel.ForeColor = Color.DarkGreen;
        }
    }

    private string EffectiveHome()
    {
        var configured = string.IsNullOrWhiteSpace(config.HomeUrl) ? DefaultHome : config.HomeUrl;
        if (Uri.TryCreate(configured, UriKind.Absolute, out var c) && Uri.TryCreate(DefaultHome, UriKind.Absolute, out var d) && UrisEquivalent(c, d))
        {
            return $"{StudentPage}?session={Uri.EscapeDataString(session)}&sid={Uri.EscapeDataString(studentId)}&name={Uri.EscapeDataString(studentName)}&autostart=1";
        }
        return configured;
    }

    private void NavigateHome()
    {
        if (finished) return;
        var target = EffectiveHome();
        if (browser.CoreWebView2 is not null) browser.CoreWebView2.Navigate(target);
    }

    private bool IsAllowed(string? raw)
    {
        if (finished) return false;
        if (IsUnlocked) return Uri.TryCreate(raw, UriKind.Absolute, out var free) && free.Scheme is "http" or "https";
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var target)) return false;
        if (target.Scheme is not ("http" or "https")) return false;
        if (Uri.TryCreate(EffectiveHome(), UriKind.Absolute, out var h) && UrisEquivalent(target, h)) return true;
        foreach (var site in config.AllowedSites)
        {
            if (!Uri.TryCreate(site.Url, UriKind.Absolute, out var allowed)) continue;
            if (site.Scope == "domain") { if (string.Equals(target.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)) return true; }
            else if (site.Scope == "path")
            {
                if (!string.Equals(target.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)) continue;
                var basePath = allowed.AbsolutePath.TrimEnd('/') + "/";
                var targetPath = target.AbsolutePath.TrimEnd('/') + "/";
                if (targetPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (UrisEquivalent(target, allowed)) return true;
        }
        return false;
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

    private static ForegroundInfo GetForegroundDescription()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return new ForegroundInfo(0, "desconocida", "");
            GetWindowThreadProcessId(hwnd, out var pid);
            var titleBuilder = new StringBuilder(512); _ = GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            var name = "desconocida"; try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
            return new ForegroundInfo((int)pid, name, titleBuilder.ToString().Trim());
        }
        catch { return new ForegroundInfo(0, "desconocida", ""); }
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
    private static string CleanKey(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return new string(value.Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(max).ToArray());
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    private sealed record ForegroundInfo(int ProcessId, string ProcessName, string Title);
}

public sealed class SessionConfig
{
    public string HomeUrl { get; set; } = DefaultHomeValue;
    public List<AllowedSite> AllowedSites { get; set; } = new();
    private const string DefaultHomeValue = "https://utec-math.github.io/monitor-evaluaciones-utec/";
    public void Normalize()
    {
        if (!Uri.TryCreate(HomeUrl, UriKind.Absolute, out var h) || h.Scheme is not ("http" or "https")) HomeUrl = DefaultHomeValue;
        AllowedSites ??= new();
        AllowedSites = AllowedSites.Where(x => x is not null && Uri.TryCreate(x.Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https").Take(30).ToList();
        foreach (var x in AllowedSites) if (x.Scope is not ("exact" or "path" or "domain")) x.Scope = "exact";
    }
}

public sealed class AllowedSite { public string Url { get; set; } = ""; public string Scope { get; set; } = "exact"; }
public sealed class RemoteCommand { public string Id { get; set; } = ""; public string Action { get; set; } = ""; public long IssuedAt { get; set; } public long ExpiresAt { get; set; } public int DurationSec { get; set; } }
