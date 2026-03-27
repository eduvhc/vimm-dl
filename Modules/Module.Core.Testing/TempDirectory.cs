namespace Module.Core.Testing;

/// <summary>
/// Creates a unique temp directory for a test and deletes it on dispose.
/// Use in test base classes to get real directories for integration tests.
///
/// Usage:
///   using var tmp = new TempDirectory("MyTest");
///   var sub = tmp.CreateSubDir("completed");
///   TempDirectory.CreateFile(sub, "game.iso", 4096);
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public string Root { get; }

    public TempDirectory(string prefix = "ModuleTest")
    {
        Root = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}"[..32]);
        Directory.CreateDirectory(Root);
    }

    /// <summary>Creates a subdirectory under the root and returns its path.</summary>
    public string CreateSubDir(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates a real file with actual bytes on disk.</summary>
    public static void CreateFile(string dir, string name, long sizeBytes = 1024)
    {
        var path = Path.Combine(dir, name);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        if (sizeBytes > 0)
        {
            fs.SetLength(sizeBytes);
            var buffer = new byte[Math.Min(sizeBytes, 4096)];
            Random.Shared.NextBytes(buffer);
            fs.Write(buffer, 0, buffer.Length);
        }
    }

    /// <summary>Creates a real file with specific content.</summary>
    public static void CreateFile(string dir, string name, byte[] content)
    {
        File.WriteAllBytes(Path.Combine(dir, name), content);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { }
    }
}
