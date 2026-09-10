namespace MonitorEvaluaciones.App;

/// <summary>
/// Experiencia visual de pre-cierre para que el estudiante vea inmediatamente
/// que el Monitor está finalizando sus procesos. No cambia la lógica de cierre:
/// acompaña visualmente el cierre existente y deja el último clic al estudiante.
/// </summary>
internal static class PreCloseExperience
{
    private static readonly Color Ivory = ColorTranslator.FromHtml("#F0E7D8");
    private static readonly Color WarmCard = ColorTranslator.FromHtml("#FAF7F1");
    private static readonly Color Camel = ColorTranslator.FromHtml("#B99A75");
    private static readonly Color Tobacco = ColorTranslator.FromHtml("#765642");
    private static readonly Color DarkTobacco = ColorTranslator.FromHtml("#5D4032");
    private static readonly Color Petroleum = ColorTranslator.FromHtml("#35585A");
    private static readonly Color Charcoal = ColorTranslator.FromHtml("#2E2E2E");

    public static void Apply(Form form)
    {
        var connectedBar = form.Controls
            .OfType<FlowLayoutPanel>()
            .FirstOrDefault(p =>
                p.Controls.OfType<TextBox>().Count() == 0 &&
                p.Controls.OfType<Button>().Any(b => string.Equals(b.Text, "Inicio", StringComparison.OrdinalIgnoreCase)) &&
                p.Controls.OfType<Label>().Count() >= 2);

        if (connectedBar is null)
            return;

        var state = new CloseState(form, connectedBar);
        state.Attach();
    }

    private sealed class CloseState
    {
        private enum Stage
        {
            Idle,
            Preparing,
            Finalizing,
            Ready,
            Closing
        }

        private readonly Form form;
        private readonly FlowLayoutPanel connectedBar;
        private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 160 };
        private readonly Button prepareButton;
        private readonly Panel overlay;
        private readonly Panel card;
        private readonly RetroProgressBar progress;
        private readonly Label title;
        private readonly Label detail;
        private readonly Label stateLabel;
        private Stage stage;
        private bool previousControlBox;

        public CloseState(Form form, FlowLayoutPanel connectedBar)
        {
            this.form = form;
            this.connectedBar = connectedBar;

            prepareButton = new Button
            {
                Text = "Preparar cierre",
                AutoSize = false,
                Width = 126,
                Height = 34,
                Margin = new Padding(8, 3, 0, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Tobacco,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TabStop = false
            };
            prepareButton.FlatAppearance.BorderSize = 0;
            prepareButton.FlatAppearance.MouseOverBackColor = DarkTobacco;
            prepareButton.FlatAppearance.MouseDownBackColor = DarkTobacco;

            overlay = new Panel
            {
                BackColor = Ivory,
                Visible = false,
                TabStop = false
            };

            card = new Panel
            {
                Width = 560,
                Height = 270,
                BackColor = WarmCard,
                BorderStyle = BorderStyle.FixedSingle
            };

            var topRule = new Panel
            {
                Left = 0,
                Top = 0,
                Width = card.Width,
                Height = 7,
                BackColor = Tobacco,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var eyebrow = new Label
            {
                Text = "MONITOR DE EVALUACIONES  ·  CIERRE",
                Left = 42,
                Top = 35,
                Width = 430,
                Height = 22,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Petroleum,
                BackColor = WarmCard
            };

            title = new Label
            {
                Text = "FINALIZANDO",
                Left = 42,
                Top = 68,
                Width = 470,
                Height = 38,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Tobacco,
                BackColor = WarmCard
            };

            detail = new Label
            {
                Text = "Guardando el estado y cerrando los procesos del monitor…",
                Left = 44,
                Top = 112,
                Width = 470,
                Height = 28,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                ForeColor = Charcoal,
                BackColor = WarmCard
            };

            progress = new RetroProgressBar
            {
                Left = 44,
                Top = 157,
                Width = 470,
                Height = 30,
                BackColor = WarmCard
            };

            stateLabel = new Label
            {
                Text = "PREPARANDO CIERRE",
                Left = 44,
                Top = 205,
                Width = 300,
                Height = 22,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Camel,
                BackColor = WarmCard
            };

            var version = new Label
            {
                Text = "UTEC  ·  v0.9",
                AutoSize = true,
                Left = 432,
                Top = 205,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Tobacco,
                BackColor = WarmCard
            };

            // Pequeños bloques geométricos que repiten el lenguaje visual retro
            // usado en el resto de la interfaz, sin recurrir a imágenes externas.
            var block1 = new Panel { Left = 484, Top = 38, Width = 10, Height = 10, BackColor = Camel };
            var block2 = new Panel { Left = 499, Top = 38, Width = 10, Height = 10, BackColor = Tobacco };
            var block3 = new Panel { Left = 514, Top = 38, Width = 10, Height = 10, BackColor = Petroleum };

            card.Controls.Add(topRule);
            card.Controls.Add(eyebrow);
            card.Controls.Add(title);
            card.Controls.Add(detail);
            card.Controls.Add(progress);
            card.Controls.Add(stateLabel);
            card.Controls.Add(version);
            card.Controls.Add(block1);
            card.Controls.Add(block2);
            card.Controls.Add(block3);
            overlay.Controls.Add(card);
        }

        public void Attach()
        {
            connectedBar.Controls.Add(prepareButton);
            form.Controls.Add(overlay);
            form.Resize += (_, _) => LayoutOverlay();
            prepareButton.Click += (_, _) =>
            {
                if (stage == Stage.Idle)
                    form.Close();
            };

            animationTimer.Tick += (_, _) =>
            {
                if (stage is not (Stage.Preparing or Stage.Finalizing))
                    return;

                if (progress.Value < RetroProgressBar.SegmentCount - 2)
                    progress.Value++;
                else
                    progress.Pulse = !progress.Pulse;
            };

            // Este evento se agrega después del manejador original de MainForm.
            // Primer cierre: mostramos la espera. Segundo cierre programático de
            // MainForm: sus tareas principales ya terminaron; antes de mostrar
            // "Listo" damos tiempo a que cualquier subida de clip ya iniciada
            // alcance un estado estable. Tercer cierre: se permite de inmediato.
            form.FormClosing += OnFormClosing;
            LayoutOverlay();
        }

        private async void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            // En la pantalla de ingreso no hace falta pre-cierre: todavía no hay
            // una evaluación activa ni procesos de monitoreo que expliquen una espera.
            if (!connectedBar.Visible || stage == Stage.Closing)
                return;

            if (stage == Stage.Idle)
            {
                stage = Stage.Preparing;
                e.Cancel = true;
                ShowPreparing();
                return;
            }

            if (stage == Stage.Preparing)
            {
                // MainForm terminó su secuencia de cierre y vuelve a llamar Close().
                // Retenemos ese cierre mientras comprobamos que el callback del último
                // clip ya fue atendido y que su intento de subida terminó.
                stage = Stage.Finalizing;
                e.Cancel = true;
                await WaitForPendingClipWorkAsync();
                stage = Stage.Ready;
                ShowReady();
                return;
            }

            if (stage == Stage.Finalizing)
            {
                e.Cancel = true;
                return;
            }

            if (stage == Stage.Ready)
            {
                stage = Stage.Closing;
                animationTimer.Stop();
                form.ControlBox = previousControlBox;
                overlay.Visible = false;
                e.Cancel = false;
            }
        }

        private async Task WaitForPendingClipWorkAsync()
        {
            // El guardado de un clip notifica a MainForm mediante BeginInvoke.
            // Este marcador se coloca detrás de esos callbacks en la cola de UI:
            // cuando se ejecuta, cualquier clip recién guardado ya fue registrado
            // como pendiente antes de comenzar su subida.
            await DrainPostedUiWorkAsync();

            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            var noUploadDeadline = DateTimeOffset.UtcNow.AddSeconds(15);
            var sawUploadBusy = false;

            while (DateTimeOffset.UtcNow < deadline)
            {
                var pending = GetPendingUploadCount();
                if (pending <= 0)
                    return;

                var gateCount = GetUploadGateCount();
                if (gateCount == 0)
                {
                    sawUploadBusy = true;
                }
                else if (sawUploadBusy)
                {
                    // El intento terminó. Si el clip continúa pendiente significa que
                    // la subida falló; el archivo local se conserva y no retenemos al
                    // estudiante innecesariamente.
                    return;
                }

                // Si por alguna razón el callback quedó esperando red/autenticación
                // antes de alcanzar la subida, tampoco bloqueamos el cierre sin límite.
                if (!sawUploadBusy && DateTimeOffset.UtcNow >= noUploadDeadline)
                    return;

                await Task.Delay(120);
            }
        }

        private Task DrainPostedUiWorkAsync()
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                form.BeginInvoke(new Action(() => completion.TrySetResult(true)));
            }
            catch
            {
                completion.TrySetResult(true);
            }
            return completion.Task;
        }

        private int GetPendingUploadCount()
        {
            try
            {
                var field = form.GetType().GetField("pendingUploads",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                return field?.GetValue(form) is System.Collections.IDictionary dictionary
                    ? dictionary.Count
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private int GetUploadGateCount()
        {
            try
            {
                var field = form.GetType().GetField("uploadGate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                return field?.GetValue(form) is SemaphoreSlim gate ? gate.CurrentCount : 1;
            }
            catch
            {
                return 1;
            }
        }

        private void ShowPreparing()
        {
            previousControlBox = form.ControlBox;
            form.ControlBox = false;
            progress.Value = 2;
            progress.Pulse = false;
            title.Text = "FINALIZANDO";
            detail.Text = "Guardando el estado y cerrando los procesos del monitor…";
            stateLabel.Text = "PREPARANDO CIERRE";
            stateLabel.ForeColor = Camel;
            LayoutOverlay();
            overlay.Visible = true;
            overlay.BringToFront();
            animationTimer.Start();
        }

        private void ShowReady()
        {
            animationTimer.Stop();
            progress.Pulse = false;
            progress.Value = RetroProgressBar.SegmentCount;
            title.Text = "CIERRE PREPARADO";
            detail.Text = "Todo listo. Ahora podés cerrar la ventana.";
            stateLabel.Text = "PROCESOS FINALIZADOS";
            stateLabel.ForeColor = Petroleum;
            form.ControlBox = previousControlBox;
            overlay.Visible = true;
            overlay.BringToFront();
        }

        private void LayoutOverlay()
        {
            overlay.Bounds = form.ClientRectangle;
            card.Left = Math.Max(0, (overlay.ClientSize.Width - card.Width) / 2);
            card.Top = Math.Max(0, (overlay.ClientSize.Height - card.Height) / 2);
        }
    }

    /// <summary>
    /// Barra retro segmentada, deliberadamente sencilla: solo pinta pequeños
    /// bloques marrones. No usa animaciones pesadas, imágenes ni librerías extra.
    /// </summary>
    private sealed class RetroProgressBar : Control
    {
        public const int SegmentCount = 18;
        private int value;
        private bool pulse;

        public int Value
        {
            get => value;
            set
            {
                this.value = Math.Clamp(value, 0, SegmentCount);
                Invalidate();
            }
        }

        public bool Pulse
        {
            get => pulse;
            set
            {
                pulse = value;
                Invalidate();
            }
        }

        public RetroProgressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var outer = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using var borderPen = new Pen(Tobacco, 2F);
            e.Graphics.DrawRectangle(borderPen, outer);

            const int gap = 3;
            const int inset = 5;
            var usableWidth = Math.Max(1, Width - inset * 2 - gap * (SegmentCount - 1));
            var segmentWidth = Math.Max(2, usableWidth / SegmentCount);
            var segmentHeight = Math.Max(3, Height - inset * 2);

            using var emptyBrush = new SolidBrush(Color.FromArgb(225, 213, 197));
            using var camelBrush = new SolidBrush(Camel);
            using var tobaccoBrush = new SolidBrush(Tobacco);
            using var darkBrush = new SolidBrush(DarkTobacco);

            for (var i = 0; i < SegmentCount; i++)
            {
                var x = inset + i * (segmentWidth + gap);
                var rect = new Rectangle(x, inset, segmentWidth, segmentHeight);

                if (i >= value)
                {
                    e.Graphics.FillRectangle(emptyBrush, rect);
                    continue;
                }

                // Alternancia sutil de marrones para reforzar el aspecto retro.
                var brush = (i % 3) switch
                {
                    0 => camelBrush,
                    1 => tobaccoBrush,
                    _ => darkBrush
                };
                e.Graphics.FillRectangle(brush, rect);
            }

            if (pulse && value > 0)
            {
                var i = Math.Min(value - 1, SegmentCount - 1);
                var x = inset + i * (segmentWidth + gap);
                var rect = new Rectangle(x, inset, segmentWidth, segmentHeight);
                e.Graphics.FillRectangle(camelBrush, rect);
            }
        }
    }
}
