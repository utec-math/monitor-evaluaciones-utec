using System.Drawing.Drawing2D;

namespace MonitorEvaluaciones.App;

/// <summary>
/// Ajuste exclusivamente visual de la pantalla inicial de la v0.9.
/// Mantiene los mismos TextBox, Button, eventos y lógica de conexión.
/// </summary>
internal static class CenteredLoginLayout
{
    // Paleta "Tabaco + petróleo" aprobada para la pantalla inicial.
    private static readonly Color Ivory = Color.FromArgb(240, 231, 216);       // #F0E7D8
    private static readonly Color Camel = Color.FromArgb(185, 154, 117);       // #B99A75
    private static readonly Color Tobacco = Color.FromArgb(118, 86, 66);       // #765642
    private static readonly Color Petroleum = Color.FromArgb(53, 88, 90);      // #35585A
    private static readonly Color Coal = Color.FromArgb(46, 46, 46);           // #2E2E2E
    private static readonly Color Surface = Color.FromArgb(250, 247, 241);
    private static readonly Color Muted = Color.FromArgb(139, 113, 83);

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

        form.BackColor = Ivory;

        // Fondo decorativo: no reemplaza el WebView2 ni modifica su lógica.
        // Simplemente queda por encima durante el ingreso y se oculta al conectar.
        var backdrop = new RetroBackdrop
        {
            Dock = DockStyle.Fill,
            BackColor = Ivory,
            TabStop = false
        };
        form.Controls.Add(backdrop);
        backdrop.BringToFront();

        // El panel original sigue siendo el mismo control que usa MainForm.
        // Solo cambia su aspecto y ubicación.
        entryBar.Dock = DockStyle.None;
        entryBar.AutoSize = false;
        entryBar.Size = new Size(540, 600);
        entryBar.FlowDirection = FlowDirection.TopDown;
        entryBar.WrapContents = false;
        entryBar.Padding = new Padding(40, 30, 40, 28);
        entryBar.Margin = Padding.Empty;
        entryBar.BackColor = Surface;
        entryBar.BorderStyle = BorderStyle.None;

        var icon = new ClipboardCheckGlyph
        {
            Width = 460,
            Height = 72,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Surface
        };

        var title = new Label
        {
            Text = "Monitor de Evaluaciones",
            AutoSize = false,
            Width = 460,
            Height = 46,
            Font = new Font("Segoe UI", 21F, FontStyle.Bold),
            ForeColor = Tobacco,
            BackColor = Surface,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = Padding.Empty
        };

        var subtitle = new Label
        {
            Text = "Ingresa tus datos para continuar",
            AutoSize = false,
            Width = 460,
            Height = 34,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
            ForeColor = Muted,
            BackColor = Surface,
            TextAlign = ContentAlignment.TopCenter,
            Margin = new Padding(0, 0, 0, 12)
        };

        entryBar.Controls.Add(icon);
        entryBar.Controls.Add(title);
        entryBar.Controls.Add(subtitle);
        entryBar.Controls.SetChildIndex(icon, 0);
        entryBar.Controls.SetChildIndex(title, 1);
        entryBar.Controls.SetChildIndex(subtitle, 2);

        string[] fieldNames = { "Sesión:", "Nombre:", "Documento:" };
        string[] placeholders = { "Ingrese la sesión...", "Ingrese su nombre...", "Ingrese su documento..." };

        for (var i = 0; i < 3; i++)
        {
            var label = labels[i];
            label.Text = fieldNames[i];
            label.AutoSize = false;
            label.Width = 460;
            label.Height = 22;
            label.Padding = Padding.Empty;
            label.Margin = new Padding(0, i == 0 ? 0 : 10, 0, 3);
            label.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            label.ForeColor = Coal;
            label.BackColor = Surface;
            label.TextAlign = ContentAlignment.BottomLeft;

            var box = textBoxes[i];
            box.AutoSize = false;
            box.Width = 460;
            box.Height = 42;
            box.Margin = Padding.Empty;
            box.Font = new Font("Segoe UI", 11F, FontStyle.Regular);
            box.ForeColor = Coal;
            box.BackColor = Color.FromArgb(255, 253, 249);
            box.BorderStyle = BorderStyle.FixedSingle;
            box.PlaceholderText = placeholders[i];
        }

        connectButton.Text = "Ingresar al monitor";
        connectButton.AutoSize = false;
        connectButton.Width = 460;
        connectButton.Height = 50;
        connectButton.Margin = new Padding(0, 20, 0, 0);
        connectButton.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
        connectButton.ForeColor = Color.White;
        connectButton.BackColor = Petroleum;
        connectButton.FlatStyle = FlatStyle.Flat;
        connectButton.FlatAppearance.BorderSize = 0;
        connectButton.Cursor = Cursors.Hand;
        connectButton.UseVisualStyleBackColor = false;

        entryBar.Controls.SetChildIndex(labels[0], 3);
        entryBar.Controls.SetChildIndex(textBoxes[0], 4);
        entryBar.Controls.SetChildIndex(labels[1], 5);
        entryBar.Controls.SetChildIndex(textBoxes[1], 6);
        entryBar.Controls.SetChildIndex(labels[2], 7);
        entryBar.Controls.SetChildIndex(textBoxes[2], 8);
        entryBar.Controls.SetChildIndex(connectButton, 9);

        void UpdateRoundedRegions()
        {
            using (var cardPath = RoundedRectangle(new Rectangle(0, 0, entryBar.Width - 1, entryBar.Height - 1), 18))
            {
                entryBar.Region?.Dispose();
                entryBar.Region = new Region(cardPath);
            }

            using var buttonPath = RoundedRectangle(new Rectangle(0, 0, connectButton.Width, connectButton.Height), 12);
            connectButton.Region?.Dispose();
            connectButton.Region = new Region(buttonPath);
        }

        entryBar.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRectangle(new Rectangle(1, 1, entryBar.Width - 3, entryBar.Height - 3), 18);
            using var pen = new Pen(Camel, 1.15F);
            e.Graphics.DrawPath(pen, path);
        };

        entryBar.Resize += (_, _) => UpdateRoundedRegions();
        connectButton.Resize += (_, _) => UpdateRoundedRegions();

        void CenterEntryPanel()
        {
            var x = Math.Max(20, (form.ClientSize.Width - entryBar.Width) / 2);
            var y = Math.Max(20, (form.ClientSize.Height - entryBar.Height) / 2 - 6);
            entryBar.Location = new Point(x, y);
        }

        entryBar.VisibleChanged += (_, _) =>
        {
            backdrop.Visible = entryBar.Visible;
            if (entryBar.Visible)
            {
                backdrop.BringToFront();
                entryBar.BringToFront();
            }
        };

        form.Resize += (_, _) => CenterEntryPanel();
        form.Shown += (_, _) =>
        {
            CenterEntryPanel();
            backdrop.BringToFront();
            entryBar.BringToFront();
        };

        form.AcceptButton = connectButton;
        UpdateRoundedRegions();
        CenterEntryPanel();
        backdrop.BringToFront();
        entryBar.BringToFront();
    }

    private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        var arc = new Rectangle(rect.X, rect.Y, d, d);
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - d;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - d;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class RetroBackdrop : Panel
    {
        public RetroBackdrop()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Arcos casi imperceptibles, como en el diseño aprobado.
            using var softPen1 = new Pen(Color.FromArgb(24, Camel), 34F);
            using var softPen2 = new Pen(Color.FromArgb(16, Camel), 22F);
            g.DrawEllipse(softPen1, -520, ClientSize.Height - 280, 760, 760);
            g.DrawEllipse(softPen2, ClientSize.Width - 260, -80, 720, 720);

            // Pequeños píxeles decorativos en diagonal.
            using var pixelBrush = new SolidBrush(Color.FromArgb(120, Camel));
            DrawPixelCorner(g, 42, 46, pixelBrush, false);
            DrawPixelCorner(g, ClientSize.Width - 86, ClientSize.Height - 126, pixelBrush, true);

            // Pie inferior discreto.
            using var footerFont = new Font("Segoe UI", 7.5F, FontStyle.Regular);
            using var footerBrush = new SolidBrush(Color.FromArgb(180, Camel));
            var footer = "E D U C A C I Ó N   ·   E V A L U A C I Ó N   ·   O P O R T U N I D A D E S";
            g.DrawString(footer, footerFont, footerBrush, 34, Math.Max(8, ClientSize.Height - 28));

            var version = "v0.9";
            var versionSize = g.MeasureString(version, footerFont);
            g.DrawString(version, footerFont, footerBrush,
                ClientSize.Width - versionSize.Width - 28,
                Math.Max(8, ClientSize.Height - 28));

            using var footerPen = new Pen(Color.FromArgb(130, Camel), 1F);
            g.DrawLine(footerPen, 350, ClientSize.Height - 21,
                Math.Max(360, ClientSize.Width - 80), ClientSize.Height - 21);
        }

        private static void DrawPixelCorner(Graphics g, int x, int y, Brush brush, bool reverse)
        {
            const int s = 16;
            if (!reverse)
            {
                g.FillRectangle(brush, x + s, y, s, s);
                g.FillRectangle(brush, x, y + s, s, s);
                g.FillRectangle(brush, x + (2 * s), y + (2 * s), s, s);
            }
            else
            {
                g.FillRectangle(brush, x + (2 * s), y, s, s);
                g.FillRectangle(brush, x + s, y + s, s, s);
                g.FillRectangle(brush, x, y + (2 * s), s, s);
            }
        }
    }

    private sealed class ClipboardCheckGlyph : Control
    {
        public ClipboardCheckGlyph()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var cx = Width / 2;
            using var tobaccoPen = new Pen(Tobacco, 4F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var thinPen = new Pen(Color.FromArgb(180, Tobacco), 3F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            var body = new Rectangle(cx - 38, 18, 70, 48);
            g.DrawRectangle(tobaccoPen, body);
            g.DrawLine(thinPen, cx - 22, 38, cx + 8, 38);
            g.DrawLine(thinPen, cx - 22, 49, cx - 2, 49);

            using var clipPath = RoundedRectangle(new Rectangle(cx - 17, 8, 34, 18), 6);
            using var clipBrush = new SolidBrush(Surface);
            g.FillPath(clipBrush, clipPath);
            g.DrawPath(tobaccoPen, clipPath);

            using var circleBrush = new SolidBrush(Petroleum);
            g.FillEllipse(circleBrush, cx + 10, 36, 42, 42);

            using var checkPen = new Pen(Color.White, 4F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLines(checkPen, new[]
            {
                new Point(cx + 20, 57),
                new Point(cx + 29, 65),
                new Point(cx + 43, 49)
            });
        }
    }
}
