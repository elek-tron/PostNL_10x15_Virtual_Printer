using System.Diagnostics;

namespace PostNL10x15.Worker;

internal sealed class PostScriptConverter
{
    private static readonly TimeSpan ConversionTimeout =
        TimeSpan.FromSeconds(90);

    public void ConvertToPdf(string inputPath, string outputPath)
    {
        string executable = FindGhostscript();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-dSAFER");
        startInfo.ArgumentList.Add("-dBATCH");
        startInfo.ArgumentList.Add("-dNOPAUSE");
        startInfo.ArgumentList.Add("-sDEVICE=pdfwrite");
        startInfo.ArgumentList.Add("-dCompatibilityLevel=1.7");
        startInfo.ArgumentList.Add("-dAutoRotatePages=/None");
        startInfo.ArgumentList.Add("-sOutputFile=" + outputPath);
        startInfo.ArgumentList.Add(inputPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Ghostscript kon niet worden gestart.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)ConversionTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                "De omzetting van PostScript naar PDF duurde te lang.");
        }

        Task.WaitAll(output, error);
        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidDataException(
                "Ghostscript kon de afdrukgegevens niet omzetten naar PDF. "
                + error.Result.Trim()
                + Environment.NewLine
                + output.Result.Trim());
        }
    }

    private static string FindGhostscript()
    {
        string bundled = Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "Ghostscript",
            "bin",
            "gswin64c.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        string pdf24 = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "PDF24",
            "gs",
            "bin",
            "gswin64c.exe");
        if (File.Exists(pdf24))
        {
            return pdf24;
        }

        string ghostscriptRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "gs");
        if (Directory.Exists(ghostscriptRoot))
        {
            string? installed = Directory
                .EnumerateFiles(
                    ghostscriptRoot,
                    "gswin64c.exe",
                    SearchOption.AllDirectories)
                .OrderDescending()
                .FirstOrDefault();
            if (installed is not null)
            {
                return installed;
            }
        }

        throw new FileNotFoundException(
            "De PostScript-omzetter ontbreekt. Installeer PDF24 of "
            + "plaats Ghostscript in Tools\\Ghostscript.");
    }
}
