namespace MonitorEvaluaciones.App;

/// <summary>
/// Ajuste puramente visual y conservador de la pantalla inicial.
/// No reemplaza controles, no cambia eventos y no toca la lógica de conexión.
/// Solo reorganiza el panel de ingreso existente y lo centra.
/// </summary>
internal static class CenteredLoginLayout
{
    private static readonly Color Ink = Color.FromArgb(28, 53, 86);
    private static readonly Color Muted = Color.FromArgb(103, 119, 139);
    private static readonly Color Accent = Color.FromArgb(43, 121, 218);
    private static readonly Color Border = Color.FromArgb(214, 222, 232);

    public static void Apply(Form form)
    {
        var entryBar = form.Controls
            .OfType<FlowLayoutPanel>()
            .FirstOrDefault(p => p.Controls.OfType<TextBox>().Count() == 3);

        if (entryBar is null)
            return;

        var textBoxes = entryBar.Controls.OfType<TextBox>().ToList();
        var labels = entryBar.Controls.OfType<Label>().ToList();
        var connectButton = entryBar.Controls.OfType<Button>().FirstOrDefault();

        if (textBoxes.Count != 3 || labels.Count < 3 || connectButton is null)
            return;

        // El panel original sigue siendo el mismo: simplemente deja de estar
        // anclado arriba y pasa a comportarse como una tarjeta central.
        entryBar.Dock = DockStyle.None;
        entryBar.AutoSize = false;
        entryBar.Size = new Size(520, 420);
        entryBar.FlowDirection = FlowDirection.TopDown;
        entryBar.WrapContents = false;
        entryBar.Padding = new Padding(38, 28, 38, 26);
        entryBar.Margin = Padding.Empty;
        entryBar.BackColor = Color.White;
        entryBar.BorderStyle = BorderStyle.FixedSingle;

        var title = new Label
        {
            Text = "Monitor de Evaluaciones",
            AutoSize = false,
            Width = 442,
            Height = 48,
            Font = new Font("Segoe UI", 21F, FontStyle.Bold),
            ForeColor = Ink,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 0)
        };

        var subtitle = new Label
        {
            Text = "Completá tus datos para ingresar",
            AutoSize = false,
            Width = 442,
            Height = 34,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
            ForeColor = Muted,
            TextAlign = ContentAlignment.TopCenter,
            Margin = new Padding(0, 0, 0, 8)
        };

        entryBar.Controls.Add(title);
        entryBar.Controls.SetChildIndex(title, 0);
        entryBar.Controls.Add(subtitle);
        entryBar.Controls.SetChildIndex(subtitle, 1);

        string[] fieldNames = { "Sesión", "Nombre", "Documento" };
        string[] placeholders = { "Ingrese la sesión...", "Ingrese su nombre...", "Ingrese su documento..." };

        for (var i = 0; i < 3; i++)
        {
            var label = labels[i];
            label.Text = fieldNames[i];
            label.AutoSize = false;
            label.Width = 442;
            label.Height = 22;
            label.Padding = Padding.Empty;
            label.Margin = new Padding(0, i == 0 ? 0 : 8, 0, 2);
            label.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            label.ForeColor = Ink;
            label.TextAlign = ContentAlignment.BottomLeft;

            var box = textBoxes[i];
            box.AutoSize = false;
            box.Width = 442;
            box.Height = 38;
            box.Margin = new Padding(0, 0, 0, 0);
            box.Font = new Font("Segoe UI", 11F, FontStyle.Regular);
            box.ForeColor = Color.FromArgb(38, 55, 74);
            box.BackColor = Color.White;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.PlaceholderText = placeholders[i];
        }

        connectButton.Text = "Ingresar al monitor";
        connectButton.AutoSize = false;
        connectButton.Width = 442;
        connectButton.Height = 46;
        connectButton.Margin = new Padding(0, 18, 0, 0);
        connectButton.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
        connectButton.ForeColor = Color.White;
        connectButton.BackColor = Accent;
        connectButton.FlatStyle = FlatStyle.Flat;
        connectButton.FlatAppearance.BorderSize = 0;
        connectButton.Cursor = Cursors.Hand;
        connectButton.UseVisualStyleBackColor = false;

        // Asegura que los controles mantengan el orden vertical correcto.
        entryBar.Controls.SetChildIndex(labels[0], 2);
        entryBar.Controls.SetChildIndex(textBoxes[0], 3);
        entryBar.Controls.SetChildIndex(labels[1], 4);
        entryBar.Controls.SetChildIndex(textBoxes[1], 5);
        entryBar.Controls.SetChildIndex(labels[2], 6);
        entryBar.Controls.SetChildIndex(textBoxes[2], 7);
        entryBar.Controls.SetChildIndex(connectButton, 8);

        void CenterEntryPanel()
        {
            var x = Math.Max(20, (form.ClientSize.Width - entryBar.Width) / 2);
            var y = Math.Max(20, (form.ClientSize.Height - entryBar.Height) / 2 - 12);
            entryBar.Location = new Point(x, y);
        }

        form.Resize += (_, _) => CenterEntryPanel();
        form.Shown += (_, _) =>
        {
            CenterEntryPanel();
            entryBar.BringToFront();
        };

        form.AcceptButton = connectButton;
        CenterEntryPanel();
        entryBar.BringToFront();
    }
}
