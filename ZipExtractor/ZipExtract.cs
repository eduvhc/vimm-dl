using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ZipExtractor;

public static class ZipExtract
{
    /// <summary>
    /// Extract an archive using 7z. Optional onProgress callback receives percentage (0-100).
    /// </summary>
    public static async Task<(bool Success, string? Error)> ExtractAsync(
        string zipPath,
        string outputDir,
        Action<int>? onProgress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            return (false, $"File not found: {zipPath}");

        Directory.CreateDirectory(outputDir);

        // -bsp1 enables progress to stdout (uses \b backspace chars to update in-place)
        var psi = new ProcessStartInfo
        {
            FileName = "7z",
            Arguments = $"x \"{zipPath}\" -o\"{outputDir}\" -y -bsp1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 7z");

        var errors = new StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
        proc.BeginErrorReadLine();

        // 7z uses \b (backspace) to overwrite progress in-place, not \r or \n.
        // Read char-by-char, accumulate into segments split by \b runs or \r or \n.
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
                            var m = Regex.Match(segment.ToString(), @"(\d+)%");
                            if (m.Success)
                            {
                                var pct = int.Parse(m.Groups[1].Value);
                                if (pct != lastPct) { lastPct = pct; onProgress(pct); }
                            }
                        }
                        // On \b, clear segment (7z overwrites). On \r/\n, also clear.
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
            ? (true, null)
            : (false, $"7z failed (exit {proc.ExitCode}): {errors}");
    }
}
