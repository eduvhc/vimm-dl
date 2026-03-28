using Microsoft.Extensions.Logging.Abstractions;
using Module.Core.Testing;
using Module.Download.Bridge;
using Module.Download.Tests.Helpers;

/// <summary>
/// Tests for DownloadService state transitions with real file I/O.
/// Uses real temp directories, real file handles, real async timing.
/// </summary>
[TestClass]
public class DownloadServiceStateTests
{
    private FakeDownloadBridge _bridge = null!;
    private DownloadService _service = null!;
    private TempDirectory _tmp = null!;

    [TestInitialize]
    public void Setup()
    {
        _tmp = new TempDirectory("DownloadStateTests");
        _bridge = new FakeDownloadBridge();
        _service = new DownloadService(_bridge, NullLogger<DownloadService>.Instance,
            new FakeHttpClientFactory());
        _service.Configure(_tmp.Root);
    }

    [TestCleanup]
    public void Cleanup() => _tmp.Dispose();

    // --- Initial state ---

    [TestMethod]
    public void InitialState_AllClean()
    {
        Assert.IsFalse(_service.IsRunning);
        Assert.IsFalse(_service.IsPaused);
        Assert.IsNull(_service.CurrentFile);
        Assert.IsNull(_service.CurrentUrl);
        Assert.IsNull(_service.CurrentProgress);
        Assert.AreEqual(0, _service.TotalBytes);
        Assert.AreEqual(0, _service.DownloadedBytes);
    }

    // --- Start with empty queue ---

    [TestMethod]
    public async Task Start_EmptyQueue_CreatesDirectories_EmitsDone()
    {
        var provider = new FakeItemProvider([]);
        _service.Start(provider);
        await WaitForDone();

        Assert.IsFalse(_service.IsRunning);
        Assert.IsFalse(_service.IsPaused);
        Assert.IsTrue(Directory.Exists(Path.Combine(_tmp.Root, "downloading")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tmp.Root, "completed")));
        Assert.IsTrue(_bridge.AllEvents.Any(e => e is DownloadDoneEvent));
    }

    // --- Double start is ignored ---

    [TestMethod]
    public async Task Start_CalledTwice_OnlyOneRunLoop()
    {
        var provider = new BlockingItemProvider();
        _service.Start(provider);
        await provider.WaitUntilEntered();

        _service.Start(new FakeItemProvider([])); // ignored — already running

        Assert.IsTrue(_service.IsRunning);
        Assert.AreEqual(1, provider.EnterCount); // only entered once

        provider.Release();
        await WaitForNotRunning();
    }

    // --- Stop resets all state ---

    [TestMethod]
    public async Task Stop_ClearsRunningState()
    {
        var provider = new BlockingItemProvider();
        _service.Start(provider);
        await provider.WaitUntilEntered();

        _service.Stop();
        provider.Release();
        await WaitForNotRunning();

        Assert.IsFalse(_service.IsRunning);
        Assert.IsFalse(_service.IsPaused);
        Assert.IsNull(_service.CurrentFile);
        Assert.IsNull(_service.CurrentUrl);
    }

    // --- Pause preserves state for resume ---

    [TestMethod]
    public async Task Pause_KeepsIsPaused_ClearsIsRunning()
    {
        var provider = new BlockingItemProvider();
        _service.Start(provider);
        await provider.WaitUntilEntered();

        _service.Pause();
        provider.Release();
        await WaitForNotRunning();

        Assert.IsFalse(_service.IsRunning);
        Assert.IsTrue(_service.IsPaused);
    }

    // --- Resume after pause ---

    [TestMethod]
    public async Task Resume_AfterPause_ResetsIsPaused()
    {
        // Pause
        var provider1 = new BlockingItemProvider();
        _service.Start(provider1);
        await provider1.WaitUntilEntered();
        _service.Pause();
        provider1.Release();
        await WaitForNotRunning();
        Assert.IsTrue(_service.IsPaused);

        // Resume with empty queue
        _bridge.Clear();
        _service.Start(new FakeItemProvider([]));
        await WaitForDone();

        Assert.IsFalse(_service.IsRunning);
        Assert.IsFalse(_service.IsPaused);
    }

    // --- Stop then start = clean slate ---

    [TestMethod]
    public async Task Stop_ThenStart_CleanSlate()
    {
        var provider1 = new BlockingItemProvider();
        _service.Start(provider1);
        await provider1.WaitUntilEntered();
        _service.Stop();
        provider1.Release();
        await WaitForNotRunning();

        _bridge.Clear();
        _service.Start(new FakeItemProvider([]));
        await WaitForDone();

        Assert.IsFalse(_service.IsRunning);
        Assert.IsFalse(_service.IsPaused);
        Assert.IsTrue(_bridge.AllEvents.Any(e => e is DownloadDoneEvent));
    }

    // --- Stop when not running ---

    [TestMethod]
    public void Stop_WhenIdle_NoOp()
    {
        _service.Stop();
        Assert.IsFalse(_service.IsRunning);
        Assert.IsFalse(_service.IsPaused);
    }

    // --- Rapid start/stop stress test ---

    [TestMethod]
    public async Task RapidStartStop_10Times_NoDeadlock()
    {
        for (int i = 0; i < 10; i++)
        {
            _service.Start(new FakeItemProvider([]));
            _service.Stop();
        }

        // Give time for any pending tasks
        await Task.Delay(500);

        // Should not be stuck — either running or not, no deadlock
        if (_service.IsRunning)
            await WaitForNotRunning(timeout: 3000);

        Assert.IsFalse(_service.IsRunning);
    }

    // --- Provider error doesn't crash service ---

    [TestMethod]
    public async Task ProviderThrows_ServiceStillCompletes()
    {
        var provider = new ThrowingItemProvider(throwOnGetNext: true);
        _service.Start(provider);

        // Wait for the service to process the error (it runs asynchronously)
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (_bridge.AllEvents.Any(e => e is DownloadErrorEvent or DownloadDoneEvent)) break;
            await Task.Delay(10);
        }

        // Service should recover, not be stuck in IsRunning
        await WaitForNotRunning(timeout: 3000);
        Assert.IsFalse(_service.IsRunning);
        Assert.IsTrue(_bridge.AllEvents.Any(e => e is DownloadErrorEvent or DownloadDoneEvent));
    }

    // --- GetBasePath with active download ---

    [TestMethod]
    public async Task GetBasePath_WhileDownloading_ReturnsActiveDir()
    {
        var provider = new BlockingItemProvider();
        _service.Start(provider);
        await provider.WaitUntilEntered();

        var basePath = _service.GetBasePath();
        Assert.AreEqual(_tmp.Root, basePath);

        provider.Release();
        await WaitForNotRunning();
    }

    // --- Directory already exists ---

    [TestMethod]
    public async Task Start_DirectoriesAlreadyExist_NoError()
    {
        Directory.CreateDirectory(Path.Combine(_tmp.Root, "downloading"));
        Directory.CreateDirectory(Path.Combine(_tmp.Root, "completed"));

        _service.Start(new FakeItemProvider([]));
        await WaitForDone();

        Assert.IsFalse(_service.IsRunning);
    }

    // --- Override path ---

    [TestMethod]
    public async Task Start_WithOverridePath_UsesIt()
    {
        var altDir = _tmp.CreateSubDir("alt_downloads");
        _service.Start(new FakeItemProvider([]), altDir);
        await WaitForDone();

        Assert.IsTrue(Directory.Exists(Path.Combine(altDir, "downloading")));
        Assert.IsTrue(Directory.Exists(Path.Combine(altDir, "completed")));
    }

    // --- Helpers ---

    private async Task WaitForDone(int timeout = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (_bridge.AllEvents.Any(e => e is DownloadDoneEvent)) return;
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for DownloadDoneEvent");
    }

    private async Task WaitForNotRunning(int timeout = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (!_service.IsRunning) return;
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for IsRunning=false");
    }
}

// --- Test doubles (minimal, only faking HTTP and queue items) ---

file class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

file class FakeItemProvider(List<DownloadItem> items) : IDownloadItemProvider
{
    private int _index;
    public Task<DownloadItem?> GetNextAsync() => Task.FromResult(_index < items.Count ? items[_index++] : null);
    public Task CompleteAsync(int id, string url, string filename, string filepath) => Task.CompletedTask;
    public Task RemoveAsync(int id) => Task.CompletedTask;
}

/// <summary>Blocks GetNextAsync() and signals when entered. Deterministic, no Task.Delay.</summary>
file class BlockingItemProvider : IDownloadItemProvider
{
    private readonly ManualResetEventSlim _entered = new(false);
    private readonly ManualResetEventSlim _release = new(false);
    private int _enterCount;

    public int EnterCount => _enterCount;

    public Task WaitUntilEntered()
    {
        return Task.Run(() => _entered.Wait(TimeSpan.FromSeconds(5)));
    }

    public void Release() => _release.Set();

    public Task<DownloadItem?> GetNextAsync()
    {
        Interlocked.Increment(ref _enterCount);
        _entered.Set();
        _release.Wait(TimeSpan.FromSeconds(10));
        return Task.FromResult<DownloadItem?>(null);
    }
    public Task CompleteAsync(int id, string url, string filename, string filepath) => Task.CompletedTask;
    public Task RemoveAsync(int id) => Task.CompletedTask;
}

file class ThrowingItemProvider(bool throwOnGetNext) : IDownloadItemProvider
{
    public Task<DownloadItem?> GetNextAsync() =>
        throwOnGetNext ? throw new InvalidOperationException("DB connection lost") : Task.FromResult<DownloadItem?>(null);
    public Task CompleteAsync(int id, string url, string filename, string filepath) => Task.CompletedTask;
    public Task RemoveAsync(int id) => Task.CompletedTask;
}
