using System.Drawing.Drawing2D;
using System.Reflection;

namespace MonitorEvaluaciones.App;

/// <summary>
/// Presentational layer for the student application's initial screen.
/// It reparents the existing input controls so all MainForm connection logic
/// and event handlers remain unchanged.
/// </summary>
internal static class StudentStartScreen
{
    private static readonly Color Ink = Color.FromArgb(31, 55, 64);
    private static readonly Color Muted = Color.FromArgb(102, 122, 130);
    private static readonly Color Accent = Color.FromArgb(31, 126, 132);
    private static readonly Color AccentDark = Color.FromArgb(24, 101, 108);
    private static readonly Color Surface = Color.FromArgb(247, 250, 250);
    private static readonly Color Border = Color.FromArgb(218, 229, 231);

    public static void Apply(MainForm form)
    {
        var entryBar = GetField<FlowLayoutPanel>(form, "entryBar");
        var connectedBar = GetField<FlowLayoutPanel>(form, "connectedBar");
        var sessionBox = GetField<TextBox>(form, "sessionBox");
        var nameBox = GetField<TextBox>(form, "nameBox");
        var studentBox = GetField<TextBox>(form, "studentBox");
        var connectButton = GetField<Button>(form, "connectButton");
        var homeButton = GetField<Button>(form, "homeButton");
        var statusLabel = GetField<Label>(form, "statusLabel");
        var identityLabel = GetField<Label>(form, "identityLabel");

        form.SuspendLayout();
        try
        {
            form.Text = "Monitor de Evaluaciones · v0.9";
            form.BackColor = Surface;
            form.MinimumSize = new Size(920, 650);

            StyleConnectedBar(connectedBar, homeButton, statusLabel, identityLabel);

            var startup = new GalileoSurface
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                TabStop = false
            };

            var card = new RoundedPanel
            {
                Size = new Size(472, 532),
                BackColor = Color.White,
                BorderColor = Border,
                CornerRadius = 22,
                Padding = new Padding(42, 36, 42, 30)
            };

            var content = BuildFormContent(sessionBox, nameBox, studentBox, connectButton);
            card.Controls.Add(content);
            startup.Controls.Add(card);

            var quote = new Label
            {
                AutoSize = false,
                Size = new Size(390, 70),
                Text = "“La naturaleza está escrita en lenguaje matemático.”\r\nGalileo Galilei",
                Font = new Font("Segoe UI", 10f, FontStyle.Italic),
                ForeColor = Color.FromArgb(92, 111, 118),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            startup.Controls.Add(quote);

            var motto = new Label
            {
                AutoSize = true,
                Text = "Observar  ·  razonar  ·  demostrar",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(89, 122, 125),
                BackColor = Color.Transparent
            };
            startup.Controls.Add(motto);

            // MainForm uses entryBar.Visible=false as the signal that the
            // student has connected. Keep that behavior and mirror it here.
            form.Controls.Remove(entryBar);
            foreach (Control oldControl in entryBar.Controls.Cast<Control>().ToArray())
                oldControl.Dispose();
            entryBar.Controls.Clear();
            entryBar.Size = new Size(1, 1);
            entryBar.Margin = Padding.Empty;
            entryBar.Padding = Padding.Empty;
            entryBar.BackColor = Color.Transparent;
            startup.Controls.Add(entryBar);

            void PositionDecorations()
            {
                card.Left = Math.Max(24, (startup.ClientSize.Width - card.Width) / 2);
                card.Top = Math.Max(22, (startup.ClientSize.Height - card.Height) / 2 - 4);

                quote.Left = Math.Max(24, startup.ClientSize.Width - quote.Width - 36);
                quote.Top = Math.Max(card.Bottom + 10, startup.ClientSize.Height - quote.Height - 24);

                motto.Left = 34;
                motto.Top = Math.Max(22, startup.ClientSize.Height - motto.Height - 30);
            }

            startup.Resize += (_, _) => PositionDecorations();
            entryBar.VisibleChanged += (_, _) => startup.Visible = entryBar.Visible;

            form.Controls.Add(startup);
            startup.BringToFront();
            PositionDecorations();

            form.AcceptButton = connectButton;
        }
        finally
        {
            form.ResumeLayout(true);
        }
    }

    private static TableLayoutPanel BuildFormContent(
        TextBox sessionBox,
        TextBox nameBox,
        TextBox studentBox,
        Button connectButton)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 14,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 38);
        AddRow(layout, 48);
        AddRow(layout, 10);
        AddRow(layout, 22);
        AddRow(layout, 38);
        AddRow(layout, 14);
        AddRow(layout, 22);
        AddRow(layout, 38);
        AddRow(layout, 14);
        AddRow(layout, 22);
        AddRow(layout, 38);
        AddRow(layout, 24);
        AddRow(layout, 46);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Monitor de Evaluaciones",
            Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold),
            ForeColor = Ink,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        layout.Controls.Add(title, 0, 0);

        var subtitle = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Ingresá tus datos para conectarte a la sesión indicada por el docente.",
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = Muted,
            TextAlign = ContentAlignment.TopLeft,
            Margin = Padding.Empty
        };
        layout.Controls.Add(subtitle, 0, 1);

        layout.Controls.Add(MakeFieldLabel("Código de sesión"), 0, 3);
        StyleTextBox(sessionBox, "Ej.: MAT1-P1");
        layout.Controls.Add(sessionBox, 0, 4);

        layout.Controls.Add(MakeFieldLabel("Nombre y apellido"), 0, 6);
        StyleTextBox(nameBox, "Tu nombre completo");
        layout.Controls.Add(nameBox, 0, 7);

        layout.Controls.Add(MakeFieldLabel("Documento"), 0, 9);
        StyleTextBox(studentBox, "Cédula o identificador");
        layout.Controls.Add(studentBox, 0, 10);

        connectButton.Text = "Ingresar al monitor de evaluaciones";
        connectButton.AutoSize = false;
        connectButton.Dock = DockStyle.Fill;
        connectButton.Margin = Padding.Empty;
        connectButton.FlatStyle = FlatStyle.Flat;
        connectButton.FlatAppearance.BorderSize = 0;
        connectButton.BackColor = Accent;
        connectButton.ForeColor = Color.White;
        connectButton.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        connectButton.Cursor = Cursors.Hand;
        connectButton.UseVisualStyleBackColor = false;
        connectButton.MouseEnter += (_, _) => connectButton.BackColor = AccentDark;
        connectButton.MouseLeave += (_, _) => connectButton.BackColor = Accent;
        layout.Controls.Add(connectButton, 0, 12);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "La conexión y el estado de supervisión se mostrarán al ingresar.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(123, 139, 145),
            TextAlign = ContentAlignment.BottomCenter,
            Margin = new Padding(0, 12, 0, 0)
        };
        layout.Controls.Add(hint, 0, 13);

        sessionBox.TabIndex = 0;
        nameBox.TabIndex = 1;
        studentBox.TabIndex = 2;
        connectButton.TabIndex = 3;

        return layout;
    }

    private static void StyleTextBox(TextBox box, string placeholder)
    {
        box.Dock = DockStyle.Fill;
        box.Margin = Padding.Empty;
        box.Font = new Font("Segoe UI", 11f);
        box.ForeColor = Ink;
        box.BackColor = Color.White;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.PlaceholderText = placeholder;
    }

    private static Label MakeFieldLabel(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
        ForeColor = Color.FromArgb(65, 86, 94),
        TextAlign = ContentAlignment.BottomLeft,
        Margin = Padding.Empty
    };

    private static void StyleConnectedBar(
        FlowLayoutPanel connectedBar,
        Button homeButton,
        Label statusLabel,
        Label identityLabel)
    {
        connectedBar.Height = 58;
        connectedBar.Padding = new Padding(14, 10, 14, 8);
        connectedBar.BackColor = Color.FromArgb(239, 247, 246);

        statusLabel.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
        identityLabel.Font = new Font("Segoe UI", 9.5f);

        homeButton.FlatStyle = FlatStyle.Flat;
        homeButton.FlatAppearance.BorderColor = Color.FromArgb(188, 211, 212);
        homeButton.FlatAppearance.BorderSize = 1;
        homeButton.BackColor = Color.White;
        homeButton.ForeColor = AccentDark;
        homeButton.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        homeButton.Cursor = Cursors.Hand;
    }

    private static void AddRow(TableLayoutPanel layout, float height) =>
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

    private static T GetField<T>(object instance, string name) where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(instance) as T
               ?? throw new InvalidOperationException($"No se encontró el control interno '{name}'.");
    }

    private sealed class GalileoSurface : Panel
    {
        public GalileoSurface()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var wash = new SolidBrush(Color.FromArgb(22, 66, 156, 157));
            g.FillEllipse(wash, ClientSize.Width - 420, -130, 560, 560);

            using var orbitPen = new Pen(Color.FromArgb(38, 47, 126, 132), 1.4f);
            g.DrawEllipse(orbitPen, ClientSize.Width - 350, 92, 260, 150);
            g.DrawEllipse(orbitPen, ClientSize.Width - 318, 62, 196, 210);

            DrawTelescope(g, new Point(ClientSize.Width - 175, 255));

            using var formulaFont = new Font("Cambria Math", 20f, FontStyle.Regular);
            using var formulaBrush = new SolidBrush(Color.FromArgb(32, 43, 108, 113));
            g.DrawString("s = ½gt²", formulaFont, formulaBrush, 46, 54);
            g.DrawString("v = gt", formulaFont, formulaBrush, ClientSize.Width - 255, ClientSize.Height - 185);

            using var galileoFont = new Font("Segoe UI Semibold", 46f, FontStyle.Bold);
            using var galileoBrush = new SolidBrush(Color.FromArgb(20, 31, 99, 105));
            g.DrawString("GALILEO", galileoFont, galileoBrush, ClientSize.Width - 320, 300);
        }

        private static void DrawTelescope(Graphics g, Point origin)
        {
            using var line = new Pen(Color.FromArgb(58, 35, 103, 108), 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var fine = new Pen(Color.FromArgb(46, 35, 103, 108), 1.6f);

            g.DrawLine(line, origin.X - 52, origin.Y - 45, origin.X + 28, origin.Y - 70);
            g.DrawEllipse(fine, origin.X - 62, origin.Y - 51, 18, 18);
            g.DrawEllipse(fine, origin.X + 22, origin.Y - 77, 15, 15);
            g.DrawLine(line, origin.X - 2, origin.Y - 58, origin.X - 2, origin.Y + 8);
            g.DrawLine(line, origin.X - 2, origin.Y + 8, origin.X - 46, origin.Y + 70);
            g.DrawLine(line, origin.X - 2, origin.Y + 8, origin.X + 43, origin.Y + 70);
            g.DrawLine(fine, origin.X - 2, origin.Y + 8, origin.X - 4, origin.Y + 74);
        }
    }

    private sealed class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; } = 18;
        public Color BorderColor { get; set; } = Color.Gainsboro;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            using var path = RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius);
            Region?.Dispose();
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
            using var pen = new Pen(BorderColor, 1f);
            e.Graphics.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var diameter = Math.Max(2, radius * 2);
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            var path = new GraphicsPath();

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
