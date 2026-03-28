using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Module.Core;

namespace Module.Extractor;

public static partial class ZipExtract
{
    [GeneratedRegex(@"(\d+)%")]
    private static partial Regex PctRegex();

    private static readonly string SevenZipPath = Find7z();

    private static string Find7z()
    {
        if (Can7zRun("7z")) return "7z";

        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
            };
            foreach (var path in candidates)
                if (File.Exists(path)) return path;
        }

        return "7z"; // fallback — let it fail with a clear error
    }

    private static bool Can7zRun(string path)
    {
        try
        {
            using var p = Process.Start(Create7zProcess(path, "--help"));
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static ProcessStartInfo Create7zProcess(string arguments)
        => Create7zProcess(SevenZipPath, arguments);

    private static ProcessStartInfo Create7zProcess(string fileName, string arguments) => new()
    {
        FileName = fileName,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    /// <summary>
    /// Quick archive header check using 7z l (list). Only reads the archive index,
    /// not the full content — catches truncated/corrupt headers without full I/O.
    /// </summary>
    public static async Task<Result<bool>> QuickCheckAsync(
        string zipPath, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            return Result<bool>.Fail($"File not found: {zipPath}");

        using var proc = Process.Start(Create7zProcess($"l \"{zipPath}\" -y"))
            ?? throw new InvalidOperationException("Failed to start 7z");

        var errors = new StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
        proc.BeginErrorReadLine();

        await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return proc.ExitCode == 0
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail($"Archive header invalid (exit {proc.ExitCode}): {errors}");
    }

    /// <summary>
    /// Extract an archive using 7z with multithreading enabled.
    /// Optional onProgress callback receives percentage (0-100).
    /// </summary>
    public static async Task<Result<bool>> ExtractAsync(
        string zipPath,
        string outputDir,
        Action<int>? onProgress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            return Result<bool>.Fail($"File not found: {zipPath}");

        Directory.CreateDirectory(outputDir);

        using var proc = Process.Start(Create7zProcess($"x \"{zipPath}\" -o\"{outputDir}\" -y -bsp1 -mmt=on"))
            ?? throw new InvalidOperationException("Failed to start 7z");

        var errors = new StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
        proc.BeginErrorReadLine();

        var pctRegex = PctRegex();
        var progressTask = Task.Run(async () =>
        {
            var buf = new char[512];
            var segment = new StringBuilder();
            var lastPct = -1;
            var reader = proc.StandardOutput;

            while (true)
            {
                var read = await reader.ReadAsync(buf, ct);
                if (read == 0) break;

                for (var i = 0; i < read; i++)
                {
                    var c = buf[i];
                    if (c == '\b' || c == '\r' || c == '\n')
                    {
                        if (segment.Length > 0 && onProgress != null)
                        {
                            var m = pctRegex.Match(segment.ToString());
                            if (m.Success)
                            {
                                var pct = int.Parse(m.Groups[1].Value);
                                if (pct != lastPct) { lastPct = pct; onProgress(pct); }
                            }
                        }
                        if (c == '\b') { if (segment.Length > 0) segment.Remove(segment.Length - 1, 1); }
                        else segment.Clear();
                    }
                    else
                    {
                        segment.Append(c);
                    }
                }
            }
        }, ct);

        await proc.WaitForExitAsync(ct);
        try { await progressTask; } catch (OperationCanceledException) { }

        return proc.ExitCode == 0
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail($"7z failed (exit {proc.ExitCode}): {errors}");
    }
}
