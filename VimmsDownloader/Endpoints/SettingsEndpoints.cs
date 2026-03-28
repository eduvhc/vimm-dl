using System.Net;
using System.Net.Sockets;

static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // Merged: /api/config + /api/settings into one
        app.MapGet("/api/settings", async (QueueRepository repo) =>
        {
            var s = await repo.GetAllSettingsAsync();
            var platformName = OperatingSystem.IsWindows() ? "windows"
                : OperatingSystem.IsLinux() ? "linux"
                : OperatingSystem.IsMacOS() ? "macos" : "unknown";
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            return new SettingsResponse(
                Platform: platformName,
                OsDescription: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Hostname: Environment.MachineName,
                User: Environment.UserName,
                Ipv4: GetLocalIpv4(),
                DefaultPath: defaultPath,
                ActivePath: repo.GetDownloadPath(),
                FixThe: s.GetValueOrDefault(SettingsKeys.FixThe, "true") == "true",
                AddSerial: s.GetValueOrDefault(SettingsKeys.AddSerial, "true") == "true",
                StripRegion: s.GetValueOrDefault(SettingsKeys.StripRegion, "true") == "true",
                Ps3Parallelism: int.TryParse(s.GetValueOrDefault(SettingsKeys.Ps3Parallelism, "3"), out var p) ? p : 3,
                Ps3DefaultFormat: int.TryParse(s.GetValueOrDefault(SettingsKeys.Ps3DefaultFormat, "1"), out var dfp) ? dfp : 1,
                Ps3PreserveArchive: s.GetValueOrDefault(SettingsKeys.Ps3PreserveArchive, "true") == "true",
                FeatureSync: s.GetValueOrDefault(SettingsKeys.FeatureSync, "false") == "true",
                FeatureEvents: s.GetValueOrDefault(SettingsKeys.FeatureEvents, "false") == "true"
            );
        });

        app.MapPost("/api/settings", async (SettingRequest req, QueueRepository repo) =>
        {
            await repo.SaveSettingAsync(req.Key, req.Value);
            return Results.Ok();
        });

        app.MapPost("/api/settings/check-path", (SyncCompareRequest req) =>
        {
            var path = PathHelpers.ExpandPath(req.Path);
            if (string.IsNullOrEmpty(path))
                return new CheckPathResponse(path, false, false, null, "empty path");

            var exists = Directory.Exists(path);
            var writable = false;
            string? error = null;
            long? freeSpace = null;

            if (exists)
            {
                try
                {
                    writable = true;
                    try
                    {
                        var testFile = Path.Combine(path, $".vimms_wt_{Guid.NewGuid():N}");
                        using (File.Create(testFile)) { }
                        File.Delete(testFile);
                    }
                    catch { writable = false; }
                    var driveInfo = new DriveInfo(Path.GetPathRoot(path)!);
                    freeSpace = driveInfo.AvailableFreeSpace;
                }
                catch (Exception ex) { error = ex.Message; }
            }
            else
            {
                error = "Directory does not exist";
            }

            return new CheckPathResponse(path, exists, writable, freeSpace, error);
        });
    }

    private static string GetLocalIpv4()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch { return "unknown"; }
    }
}
