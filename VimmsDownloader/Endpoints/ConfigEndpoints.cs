static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config", (DownloadQueue queue, IConfiguration config) =>
        {
            var isWindows = OperatingSystem.IsWindows();
            var isLinux = OperatingSystem.IsLinux();
            var isMac = OperatingSystem.IsMacOS();
            var platformName = isWindows ? "windows" : isLinux ? "linux" : isMac ? "macos" : "unknown";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var defaultPath = Path.Combine(home, "Downloads");

            var configuredPath = config.GetValue<string>("DownloadPath");
            var activePath = string.IsNullOrWhiteSpace(configuredPath) ? defaultPath : configuredPath;

            return new ConfigResponse(platformName, System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Environment.MachineName, Environment.UserName, defaultPath, activePath,
                queue.IsRunning, queue.CurrentFile, queue.CurrentProgress);
        });

        app.MapPost("/api/config/check-path", (SetPathRequest req) =>
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
}
