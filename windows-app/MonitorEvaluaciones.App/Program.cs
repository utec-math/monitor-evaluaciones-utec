namespace MonitorEvaluaciones.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var session = args.FirstOrDefault(a => a.StartsWith("--session=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
        var student = args.FirstOrDefault(a => a.StartsWith("--id=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];

        var form = new MainForm(session, student);
        try
        {
            CenteredLoginLayout.Apply(form);
        }
        catch
        {
            // Si el ajuste visual falla por cualquier motivo, el monitor sigue
            // abriendo con la interfaz original de la versión 0.9.
        }

        Application.Run(form);
    }
}
