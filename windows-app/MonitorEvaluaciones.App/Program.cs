namespace MonitorEvaluaciones.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var session = args.FirstOrDefault(a => a.StartsWith("--session=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        Application.Run(new MainForm(session));
    }
}
