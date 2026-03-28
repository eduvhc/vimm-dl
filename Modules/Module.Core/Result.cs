namespace Module.Core;

/// <summary>
/// Generic result type for operations that can fail with an error message.
/// Struct for zero allocation on the success path.
/// </summary>
public readonly struct Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsOk => Error is null;

    private Result(T value) { Value = value; Error = null; }
    private Result(string error) { Value = default; Error = error; }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(error);
}

/// <summary>Convenience factories for void-like results.</summary>
public static class Result
{
    public static Result<bool> Ok() => Result<bool>.Ok(true);
    public static Result<bool> Fail(string error) => Result<bool>.Fail(error);
}

/// <summary>Safe file operations returning Result instead of throwing.</summary>
public static class FileOps
{
    public static Result<bool> TryMove(string source, string dest)
    {
        try
        {
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(source, dest);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public static Result<bool> TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public static Result<bool> TryDeleteDirectory(string path, bool recursive = true)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }

    public static Result<bool> TryWriteAllText(string path, string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(ex.Message); }
    }
}
