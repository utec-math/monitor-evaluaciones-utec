using System.Drawing.Imaging;
using System.Text.Json;
using OpenCvSharp;

namespace MonitorEvaluaciones.App;

public sealed class ScreenEventRecorder : IAsyncDisposable
{
    private const int Fps = 2;
    private static readonly TimeSpan PreWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PostWindow = TimeSpan.FromSeconds(30);
    private const long JpegQuality = 45L;

    private readonly object gate = new();
    private readonly Queue<FrameSample> buffer = new();
    private CancellationTokenSource? cts;
    private Task? loopTask;
    private PendingClip? pending;
    private string session = "";
    private string studentId = "";

    public bool IsRunning => cts is not null && !cts.IsCancellationRequested;
    public event Action<ClipResult>? ClipSaved;
    public event Action<string>? RecorderError;

    public void Start(string sessionCode, string student)
    {
        StopAsync().GetAwaiter().GetResult();
        session = Sanitize(sessionCode);
        studentId = Sanitize(student);
        cts = new CancellationTokenSource();
        loopTask = Task.Run(() => CaptureLoopAsync(cts.Token));
    }

    public async Task StopAsync()
    {
        var local = cts;
        cts = null;
        if (local is null) return;
        local.Cancel();
        try
        {
            if (loopTask is not null) await loopTask;
        }
        catch (OperationCanceledException) { }
        finally
        {
            local.Dispose();
            loopTask = null;
        }

        PendingClip? toSave = null;
        lock (gate)
        {
            if (pending is not null && pending.Frames.Count > 1)
            {
                toSave = pending;
                pending = null;
            }
            buffer.Clear();
        }
        if (toSave is not null) await SaveClipAsync(toSave);
    }

    public void Trigger(string reason, string detail)
    {
        if (!IsRunning) return;
        var now = DateTimeOffset.Now;
        lock (gate)
        {
            if (pending is null)
            {
                pending = new PendingClip
                {
                    TriggeredAt = now,
                    SaveAfter = now + PostWindow,
                    Reasons = new List<string> { reason },
                    Detail = detail,
                    Frames = buffer.ToList()
                };
            }
            else
            {
                pending.SaveAfter = now + PostWindow;
                if (!pending.Reasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
                    pending.Reasons.Add(reason);
                if (!string.IsNullOrWhiteSpace(detail))
                    pending.Detail = string.IsNullOrWhiteSpace(pending.Detail) ? detail : pending.Detail + " | " + detail;
            }
        }
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        var delay = TimeSpan.FromMilliseconds(1000.0 / Fps);
        while (!token.IsCancellationRequested)
        {
            var started = DateTimeOffset.Now;
            try
            {
                var frame = CaptureFrame();
                PendingClip? ready = null;
                lock (gate)
                {
                    buffer.Enqueue(frame);
                    while (buffer.Count > Fps * (int)PreWindow.TotalSeconds)
                        buffer.Dequeue();

                    if (pending is not null)
                    {
                        pending.Frames.Add(frame);
                        if (started >= pending.SaveAfter)
                        {
                            ready = pending;
                            pending = null;
                        }
                    }
                }

                if (ready is not null)
                    _ = Task.Run(() => SaveClipAsync(ready), token);
            }
            catch (Exception ex)
            {
                RecorderError?.Invoke(ex.Message);
            }

            var elapsed = DateTimeOffset.Now - started;
            var remaining = delay - elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, token);
        }
    }

    private static FrameSample CaptureFrame()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

        using var ms = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
        bmp.Save(ms, codec, ep);
        return new FrameSample(DateTimeOffset.Now, ms.ToArray());
    }

    private async Task SaveClipAsync(PendingClip clip)
    {
        try
        {
            if (clip.Frames.Count < 2) return;
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MonitorEvaluacionesUTEC", "Clips", session, studentId);
            Directory.CreateDirectory(root);
            var stamp = clip.TriggeredAt.ToString("yyyyMMdd_HHmmss");
            var safeReason = Sanitize(string.Join("_", clip.Reasons)).Trim('_');
            if (safeReason.Length > 36) safeReason = safeReason[..36];
            var file = Path.Combine(root, $"{stamp}_{safeReason}.avi");

            using var first = Cv2.ImDecode(clip.Frames[0].Jpeg, ImreadModes.Color);
            if (first.Empty()) throw new InvalidOperationException("No se pudo decodificar la captura de pantalla.");

            var targetSize = FitSize(first.Width, first.Height, 1280);
            using var writer = new VideoWriter(file, FourCC.MJPG, Fps, targetSize);
            if (!writer.IsOpened()) throw new InvalidOperationException("No se pudo crear el archivo de video.");

            foreach (var sample in clip.Frames)
            {
                using var mat = Cv2.ImDecode(sample.Jpeg, ImreadModes.Color);
                if (mat.Empty()) continue;
                if (mat.Size() == targetSize)
                    writer.Write(mat);
                else
                {
                    using var resized = new Mat();
                    Cv2.Resize(mat, resized, targetSize, 0, 0, InterpolationFlags.Area);
                    writer.Write(resized);
                }
            }
            writer.Release();

            var manifest = new
            {
                session,
                studentId,
                triggeredAt = clip.TriggeredAt,
                reasons = clip.Reasons,
                detail = clip.Detail,
                preSeconds = (int)PreWindow.TotalSeconds,
                postSeconds = (int)PostWindow.TotalSeconds,
                fps = Fps,
                audio = false,
                videoFile = Path.GetFileName(file)
            };
            await File.WriteAllTextAsync(Path.ChangeExtension(file, ".json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            ClipSaved?.Invoke(new ClipResult(file, clip.TriggeredAt, string.Join(", ", clip.Reasons), clip.Detail));
        }
        catch (Exception ex)
        {
            RecorderError?.Invoke(ex.Message);
        }
    }

    private static OpenCvSharp.Size FitSize(int width, int height, int maxWidth)
    {
        if (width <= maxWidth) return new OpenCvSharp.Size(MakeEven(width), MakeEven(height));
        var scale = maxWidth / (double)width;
        return new OpenCvSharp.Size(MakeEven(maxWidth), MakeEven((int)Math.Round(height * scale)));
    }

    private static int MakeEven(int value) => value % 2 == 0 ? value : value - 1;

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((value ?? "").Where(c => !invalid.Contains(c) && (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')).ToArray());
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed record FrameSample(DateTimeOffset At, byte[] Jpeg);

    private sealed class PendingClip
    {
        public DateTimeOffset TriggeredAt { get; set; }
        public DateTimeOffset SaveAfter { get; set; }
        public List<string> Reasons { get; set; } = new();
        public string Detail { get; set; } = "";
        public List<FrameSample> Frames { get; set; } = new();
    }
}

public sealed record ClipResult(string FilePath, DateTimeOffset TriggeredAt, string Reason, string Detail);
