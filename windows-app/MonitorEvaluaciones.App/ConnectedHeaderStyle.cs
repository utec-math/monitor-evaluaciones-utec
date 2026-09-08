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
        var connectedBar = form.Controls
            .OfType<FlowLayoutPanel>()
            .FirstOrDefault(p => !p.Visible && p.Controls.OfType<Button>().Any());

        if (connectedBar is null)
            return;

        var homeButton = connectedBar.Controls.OfType<Button>().FirstOrDefault();
        var labels = connectedBar.Controls.OfType<Label>().ToList();

        if (homeButton is null || labels.Count < 2)
            return;

        var statusLabel = labels[0];
        var identityLabel = labels[1];

        connectedBar.Dock = DockStyle.Top;
        connectedBar.Height = 62;
        connectedBar.Padding = new Padding(18, 10, 18, 8);
        connectedBar.Margin = Padding.Empty;
        connectedBar.WrapContents = false;
        connectedBar.FlowDirection = FlowDirection.LeftToRight;
        connectedBar.BackColor = Ivory;
        connectedBar.BorderStyle = BorderStyle.FixedSingle;

        var brand = new Label
        {
            Text = "Monitor de Evaluaciones",
            AutoSize = true,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Tobacco,
            BackColor = Ivory,
            Padding = new Padding(0, 7, 18, 0),
            Margin = Padding.Empty
        };

        connectedBar.Controls.Add(brand);
        connectedBar.Controls.SetChildIndex(brand, 0);

        statusLabel.BackColor = Ivory;
        statusLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        statusLabel.Padding = new Padding(0, 7, 14, 0);
        statusLabel.Margin = Padding.Empty;

        identityLabel.BackColor = Ivory;
        identityLabel.ForeColor = Charcoal;
        identityLabel.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        identityLabel.Padding = new Padding(0, 8, 16, 0);
        identityLabel.Margin = Padding.Empty;

        homeButton.Text = "Inicio";
        homeButton.AutoSize = false;
        homeButton.Width = 92;
        homeButton.Height = 36;
        homeButton.Margin = new Padding(8, 2, 0, 0);
        homeButton.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        homeButton.ForeColor = Color.White;
        homeButton.BackColor = Petroleum;
        homeButton.FlatStyle = FlatStyle.Flat;
        homeButton.FlatAppearance.BorderSize = 0;
        homeButton.FlatAppearance.MouseOverBackColor = Tobacco;
        homeButton.Cursor = Cursors.Hand;
        homeButton.UseVisualStyleBackColor = false;

        // Mantiene un separador visual discreto con la paleta aprobada.
        connectedBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Camel);
            e.Graphics.DrawLine(pen, 0, connectedBar.ClientSize.Height - 1,
                connectedBar.ClientSize.Width, connectedBar.ClientSize.Height - 1);
        };
    }
}
