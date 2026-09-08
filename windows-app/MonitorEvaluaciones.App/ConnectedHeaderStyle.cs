namespace MonitorEvaluaciones.App;

/// <summary>
/// Ajuste visual de la barra superior una vez que el estudiante está conectado.
/// No reemplaza controles ni modifica la lógica del monitor.
/// </summary>
internal static class ConnectedHeaderStyle
{
    private static readonly Color Ivory = ColorTranslator.FromHtml("#F0E7D8");
    private static readonly Color Camel = ColorTranslator.FromHtml("#B99A75");
    private static readonly Color Tobacco = ColorTranslator.FromHtml("#765642");
    private static readonly Color Petroleum = ColorTranslator.FromHtml("#35585A");
    private static readonly Color Charcoal = ColorTranslator.FromHtml("#2E2E2E");

    public static void Apply(Form form)
    {
        // Identifica la barra conectada por su contenido, no por Visible.
        // Antes de Application.Run el Form todavía no es visible y, por lo tanto,
        // usar !p.Visible podía seleccionar por error el panel de inicio de sesión.
        var connectedBar = form.Controls
            .OfType<FlowLayoutPanel>()
            .FirstOrDefault(p =>
                p.Controls.OfType<TextBox>().Count() == 0 &&
                p.Controls.OfType<Button>().Any(b => string.Equals(b.Text, "Inicio", StringComparison.OrdinalIgnoreCase)) &&
                p.Controls.OfType<Label>().Count() >= 2);

        if (connectedBar is null)
            return;

        var homeButton = connectedBar.Controls
            .OfType<Button>()
            .FirstOrDefault(b => string.Equals(b.Text, "Inicio", StringComparison.OrdinalIgnoreCase));
        var labels = connectedBar.Controls.OfType<Label>().ToList();

        if (homeButton is null || labels.Count < 2)
            return;

        var statusLabel = labels[0];
        var identityLabel = labels[1];

        connectedBar.Dock = DockStyle.Top;
        connectedBar.Height = 60;
        connectedBar.Padding = new Padding(18, 10, 18, 8);
        connectedBar.Margin = Padding.Empty;
        connectedBar.WrapContents = false;
        connectedBar.FlowDirection = FlowDirection.LeftToRight;
        connectedBar.BackColor = Ivory;
        connectedBar.BorderStyle = BorderStyle.None;

        // Marca discreta a la izquierda. Solo se agrega a la barra conectada.
        var brand = new Label
        {
            Text = "Monitor de Evaluaciones",
            AutoSize = true,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            ForeColor = Tobacco,
            BackColor = Ivory,
            Padding = new Padding(0, 8, 22, 0),
            Margin = Padding.Empty
        };

        connectedBar.Controls.Add(brand);
        connectedBar.Controls.SetChildIndex(brand, 0);

        statusLabel.BackColor = Ivory;
        statusLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        statusLabel.Padding = new Padding(0, 8, 18, 0);
        statusLabel.Margin = Padding.Empty;

        identityLabel.BackColor = Ivory;
        identityLabel.ForeColor = Charcoal;
        identityLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        identityLabel.Padding = new Padding(0, 9, 16, 0);
        identityLabel.Margin = Padding.Empty;

        homeButton.Text = "Inicio";
        homeButton.AutoSize = false;
        homeButton.Width = 88;
        homeButton.Height = 34;
        homeButton.Margin = new Padding(8, 3, 0, 0);
        homeButton.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        homeButton.ForeColor = Color.White;
        homeButton.BackColor = Petroleum;
        homeButton.FlatStyle = FlatStyle.Flat;
        homeButton.FlatAppearance.BorderSize = 0;
        homeButton.FlatAppearance.MouseOverBackColor = Tobacco;
        homeButton.Cursor = Cursors.Hand;
        homeButton.UseVisualStyleBackColor = false;

        // Separador inferior sutil con la paleta aprobada.
        connectedBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Camel);
            e.Graphics.DrawLine(pen, 0, connectedBar.ClientSize.Height - 1,
                connectedBar.ClientSize.Width, connectedBar.ClientSize.Height - 1);
        };
    }
}
