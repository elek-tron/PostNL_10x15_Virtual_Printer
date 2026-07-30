using Windows.Storage;

namespace PostNL10x15.VirtualPrinter;

internal static class EndpointLog
{
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        try
        {
            string path = Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                "endpoint-trace.log");
            lock (Sync)
            {
                File.AppendAllText(
                    path,
                    DateTimeOffset.Now.ToString("O")
                    + " "
                    + message
                    + Environment.NewLine);
            }
        }
        catch
        {
            // Logging mag een Windows-printtaak nooit laten mislukken.
        }
    }
}
