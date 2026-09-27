using Microsoft.Web.WebView2.WinForms;

namespace MonitorEvaluaciones.App;

// Separate trusted view: no navigation permissions or Firebase credentials are exposed to Moodle.
public sealed class MobilePairForm : Form
{
    private readonly WebView2 view = new() { Dock = DockStyle.Fill };

    public MobilePairForm(string url)
    {
        Text = "Vincular cámara del celular";
        Width = 520;
        Height = 660;
        StartPosition = FormStartPosition.CenterParent;
        Controls.Add(view);
        Shown += async (_, _) =>
        {
            try
            {
                await view.EnsureCoreWebView2Async();
                view.CoreWebView2.Settings.AreDevToolsEnabled = false;
                view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                view.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
                view.CoreWebView2.NavigationStarting += (_, e) =>
                {
                    if (!string.Equals(e.Uri, url, StringComparison.Ordinal)) e.Cancel = true;
                };
                view.CoreWebView2.Navigate(url);
            }
            catch { MessageBox.Show("No se pudo mostrar el QR. Pedile al docente el enlace individual."); Close(); }
        };
    }
}
