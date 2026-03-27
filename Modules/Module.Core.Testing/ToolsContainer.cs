using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Module.Core.Testing;

/// <summary>
/// Shared container with 7z, makeps3iso, and patchps3iso.
/// Bind-mounts the host temp directory so tests can read/write files
/// that the container tools can also access.
///
/// Image resolution order:
///   1. Pull ghcr.io/eduvhc/vimm-dl-tools:latest (published by CI)
///   2. Fall back to local docker build from Dockerfile.tools
/// </summary>
public sealed class ToolsContainer : IAsyncDisposable
{
    private const string RegistryImage = "ghcr.io/eduvhc/vimm-dl-tools:latest";
    private const string LocalImage = "vimm-dl-tools:local";

    private IContainer? _container;

    private static readonly string HostTempDir =
        Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static readonly SemaphoreSlim _imageLock = new(1, 1);
    private static string? _resolvedImage;

    public async Task StartAsync()
    {
        await EnsureImageAsync();
        _container = new ContainerBuilder()
            .WithImage(_resolvedImage!)
            .WithBindMount(HostTempDir, "/tmp")
            .Build();
        await _container.StartAsync();
    }

    public async Task<(long ExitCode, string Stdout, string Stderr)> ExecAsync(params string[] command)
    {
        if (_container == null) throw new InvalidOperationException("Container not started");
        var result = await _container.ExecAsync(command);
        return (result.ExitCode, result.Stdout, result.Stderr);
    }

    public static string ToContainerPath(string hostPath)
    {
        var relative = Path.GetRelativePath(HostTempDir, hostPath);
        return "/tmp/" + relative.Replace('\\', '/');
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    private static async Task EnsureImageAsync()
    {
        if (_resolvedImage != null) return;

        await _imageLock.WaitAsync();
        try
        {
            if (_resolvedImage != null) return;
            _resolvedImage = await PullOrBuildAsync();
        }
        finally
        {
            _imageLock.Release();
        }
    }

    private static async Task<string> PullOrBuildAsync()
    {
        if (await DockerExecAsync("pull", RegistryImage) == 0)
            return RegistryImage;

        var dockerfile = FindDockerfile();
        var dir = Path.GetDirectoryName(dockerfile)!;

        if (await DockerExecAsync("build", "-t", LocalImage, "-f", dockerfile, dir) == 0)
            return LocalImage;

        throw new InvalidOperationException(
            $"Failed to pull {RegistryImage} and local build also failed. Is Docker running?");
    }

    private static async Task<int> DockerExecAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi);
        if (proc == null) return -1;
        await proc.WaitForExitAsync();
        return proc.ExitCode;
    }

    private static string FindDockerfile()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "Dockerfile.tools");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }

        throw new FileNotFoundException("Dockerfile.tools not found.");
    }
}
