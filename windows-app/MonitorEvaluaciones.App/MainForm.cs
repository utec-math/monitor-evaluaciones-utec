using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MonitorEvaluaciones.App;

public sealed class MainForm : Form
{
    private const string FirebaseBase = "https://preciencia1-default-rtdb.firebaseio.com";
    private const string DefaultHome = "https://utec-math.github.io/monitor-evaluaciones-utec/";

    private readonly TextBox sessionBox = new() { Width = 190 };
    private readonly Button connectButton = new() { Text = "Conectar", AutoSize = true };
    private readonly Button homeButton = new() { Text = "Inicio", AutoSize = true };
    private readonly Label statusLabel = new() { AutoSize = true, Text = "Sin conectar" };
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer configTimer = new() { Interval = 3000 };
    private readonly HttpClient http = new();

    private string session = "";
    private SessionConfig config = new();
    private bool internalNavigation;

    public MainForm(string? initialSession)
    {
        Text = "Monitor Evaluaciones UTEC · Modo examen";
        Width = 1180;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;

        if (!string.IsNullOrWhiteSpace(initialSession))
            sessionBox.Text = CleanSession(initialSession);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 8, 10, 6),
            WrapContents = false,
            AutoSize = false
        };
        top.Controls.Add(new Label { Text = "Sesión:", AutoSize = true, Padding = new Padding(0, 7, 3, 0) });
        top.Controls.Add(sessionBox);
        top.Controls.Add(connectButton);
        top.Controls.Add(homeButton);
        top.Controls.Add(statusLabel);

        Controls.Add(browser);
        Controls.Add(top);

        connectButton.Click += async (_, _) => await ConnectAsync();
        homeButton.Click += (_, _) => NavigateHome();
        configTimer.Tick += async (_, _) => await RefreshConfigAsync(false);
        FormClosing += (_, _) => configTimer.Stop();

        Shown += async (_, _) =>
        {
            await browser.EnsureCoreWebView2Async();
            ConfigureBrowser();
            if (!string.IsNullOrWhiteSpace(sessionBox.Text))
                await ConnectAsync();
            else
                ShowMessage("Ingresá el código de sesión y presioná Conectar.");
        };
    }

    private void ConfigureBrowser()
    {
        browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        browser.CoreWebView2.Settings.IsStatusBarEnabled = false;

        browser.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (internalNavigation) return;
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
        session = CleanSession(sessionBox.Text);
        sessionBox.Text = session;
        if (string.IsNullOrWhiteSpace(session))
        {
            MessageBox.Show("Ingresá un código de sesión.", "Monitor UTEC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        statusLabel.Text = "Conectando…";
        if (await RefreshConfigAsync(true))
        {
            configTimer.Start();
            statusLabel.Text = $"Conectado · {session}";
            NavigateHome();
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
            var loaded = JsonSerializer.Deserialize<SessionConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new SessionConfig();

            loaded.Normalize();
            config = loaded;
            statusLabel.Text = $"Conectado · {session} · {config.AllowedSites.Count} recurso(s) extra";
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

    private void NavigateHome()
    {
        var target = string.IsNullOrWhiteSpace(config.HomeUrl) ? DefaultHome : config.HomeUrl;
        NavigateInternal(target);
    }

    private void NavigateInternal(string url)
    {
        if (browser.CoreWebView2 is null) return;
        internalNavigation = true;
        try { browser.CoreWebView2.Navigate(url); }
        finally { internalNavigation = false; }
    }

    private bool IsAllowed(string? raw)
    {
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
        var page = $"""
<!doctype html><html lang="es"><meta charset="utf-8"><style>
body{{font-family:Segoe UI,Arial,sans-serif;background:#eef3f5;color:#17333d;padding:48px}}
main{{max-width:720px;margin:auto;background:white;border:1px solid #dce6e9;border-radius:14px;padding:28px}}
code{{word-break:break-all;background:#f3f6f7;padding:4px 6px;border-radius:5px}}
</style><main>{body}</main></html>
""";
        internalNavigation = true;
        try { browser.NavigateToString(page); }
        finally { internalNavigation = false; }
    }

    private static string CleanSession(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return new string(value.Trim().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(60).ToArray());
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
