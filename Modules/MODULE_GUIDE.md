# Module Guide

How to create, structure, and integrate modules in this project.

## Architecture

```
Modules/
├── Module.Core/                          # Shared contracts
│   └── IModuleBridge.cs                  # Generic bridge interface
│
├── Module.Core.Testing/                  # Shared test infrastructure
│   ├── FakeBridge.cs                     # Generic fake bridge — reuse everywhere
│   └── TempDirectory.cs                  # Real temp dirs for integration tests
│
├── Module.{Name}/                        # The module
│   ├── Module.{Name}.csproj
│   ├── Bridge/
│   │   ├── I{Name}Bridge.cs             # Module-specific bridge interface
│   │   └── {Name}BridgeEvents.cs        # All events the module emits
│   ├── {Name}Models.cs                   # Public data records
│   └── {Name}Service.cs                  # Main service class
│
├── Module.{Name}.Tests/                  # Tests — real I/O, real assertions
│   ├── Module.{Name}.Tests.csproj
│   ├── Helpers/
│   │   ├── Fake{Name}Bridge.cs           # Typed alias over FakeBridge
│   │   └── {Name}TestBase.cs             # Shared setup with real resources
│   └── ...Tests.cs
```

## Step-by-step: Creating a new module

### 1. Create the project

```
mkdir -p Modules/Module.{Name}/Bridge
mkdir -p Modules/Module.{Name}.Tests/Helpers
```

**Module.{Name}.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsAotCompatible>true</IsAotCompatible>
    <RootNamespace>Module.{Name}</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0-preview.3.25171.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Module.Core\Module.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Module.{Name}.Tests" />
  </ItemGroup>
</Project>
```

**Module.{Name}.Tests.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MSTest" Version="4.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Module.{Name}\Module.{Name}.csproj" />
    <ProjectReference Include="..\Module.Core.Testing\Module.Core.Testing.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
    <Using Include="Module.{Name}" />
  </ItemGroup>
</Project>
```

Add to `VimmsDownloader.slnx`:
```xml
<Project Path="Modules/Module.{Name}/Module.{Name}.csproj" />
<Project Path="Modules/Module.{Name}.Tests/Module.{Name}.Tests.csproj" />
```

Add to `VimmsDownloader.csproj`:
```xml
<ProjectReference Include="..\Modules\Module.{Name}\Module.{Name}.csproj" />
```

### 2. Define the bridge

The bridge is how the module talks to the host. The module emits events, the host decides what to do with them.

**Bridge/{Name}BridgeEvents.cs:**
```csharp
namespace Module.{Name}.Bridge;

public abstract record {Name}Event;

public sealed record {Name}ProgressEvent(string Item, double Percent) : {Name}Event;
public sealed record {Name}CompletedEvent(string Item, bool Success, string? Error) : {Name}Event;
public sealed record {Name}StatusEvent(string Phase, string Message) : {Name}Event;
```

Rules:
- One abstract base record per module (`{Name}Event`)
- Each concrete event is a `sealed record` extending it
- Events are immutable data — no methods, no behavior
- Use positional records for conciseness

**Bridge/I{Name}Bridge.cs:**
```csharp
using Module.Core;

namespace Module.{Name}.Bridge;

public interface I{Name}Bridge : IModuleBridge<{Name}Event>;
```

One-liner. The generic constraint on `IModuleBridge<TEvent>` enforces type safety.

### 3. Write the service

```csharp
using Microsoft.Extensions.Logging;
using Module.{Name}.Bridge;

namespace Module.{Name};

public class {Name}Service
{
    private readonly I{Name}Bridge _bridge;
    private readonly ILogger<{Name}Service> _log;

    public {Name}Service(I{Name}Bridge bridge, ILogger<{Name}Service> log)
    {
        _bridge = bridge;
        _log = log;
    }

    private Task Emit({Name}Event evt) => _bridge.SendAsync(evt);

    public async Task DoWork()
    {
        await Emit(new {Name}ProgressEvent("item", 50.0));
        // ... actual work ...
        await Emit(new {Name}CompletedEvent("item", true, null));
    }
}
```

Rules:
- Constructor takes `I{Name}Bridge` and `ILogger<{Name}Service>` only
- No ASP.NET, no SignalR, no IConfiguration — framework-free
- Use a `Configure()` method for runtime settings from the host
- Use `internal` on helpers you want to test but not expose
- Private `Emit()` helper keeps call sites clean

### 4. Write tests

See the [Testing](#testing) section below for the full philosophy and patterns.

### 5. Wire up in the host

**1) SignalR bridge** — `SignalR{Name}Bridge.cs`:
```csharp
using Microsoft.AspNetCore.SignalR;
using Module.{Name}.Bridge;

class SignalR{Name}Bridge(IHubContext<DownloadHub> hub) : I{Name}Bridge
{
    public Task SendAsync({Name}Event evt) => evt switch
    {
        {Name}ProgressEvent e => hub.Clients.All.SendAsync("{Name}Progress", e),
        {Name}CompletedEvent e => hub.Clients.All.SendAsync("{Name}Completed", e),
        _ => Task.CompletedTask
    };
}
```

**2) DI registration** in `Program.cs`:
```csharp
builder.Services.AddSingleton<Module.{Name}.Bridge.I{Name}Bridge, SignalR{Name}Bridge>();
builder.Services.AddSingleton<Module.{Name}.{Name}Service>();
```

**3) JSON context** in `AppJsonContext.cs`:
```csharp
using Module.{Name};
using Module.{Name}.Bridge;

[JsonSerializable(typeof({Name}ProgressEvent))]
[JsonSerializable(typeof({Name}CompletedEvent))]
```

**4) Endpoints** in `Endpoints/{Name}Endpoints.cs` — thin layer calling the service.

---

## Testing

### Philosophy: real stuff, no mocks

Tests must exercise real behavior. This means:

- **Real files on disk** — create actual files, copy them, verify bytes match
- **Real directories** — create temp dirs, delete them mid-operation, check graceful handling
- **Real file sizes** — don't fake sizes, write actual bytes and read them back
- **Real error conditions** — delete a directory before an operation to simulate disconnection
- **Real content verification** — `CollectionAssert.AreEqual(sourceBytes, destBytes)`

The only fake is the bridge. Everything else is real.

**Do NOT mock:**
- File system operations (File.Exists, Directory.GetFiles, FileStream)
- DriveInfo or disk space checks
- The service itself
- Internal helpers

**DO fake:**
- The bridge — to capture events without needing SignalR
- ILogger — use `NullLogger<T>.Instance`, we don't assert on logs

### Shared test infrastructure: Module.Core.Testing

Every module test project references `Module.Core.Testing`. It provides two things:

**`FakeBridge<TEvent>`** — generic event capture, thread-safe:
```csharp
var bridge = new FakeBridge<SyncEvent>();

// After running service methods:
bridge.AllEvents        // all events in order
bridge.Of<T>()          // filter by type: bridge.Of<SyncCompletedEvent>()
bridge.Last<T>()        // last event of type, or null
bridge.Count            // total event count
bridge.Clear()          // reset
```

Every module test creates a typed alias:
```csharp
// Helpers/Fake{Name}Bridge.cs
using Module.Core.Testing;
using Module.{Name}.Bridge;

namespace Module.{Name}.Tests.Helpers;

public class Fake{Name}Bridge : FakeBridge<{Name}Event>, I{Name}Bridge
{
    // Convenience accessors for common event types
    public IReadOnlyList<{Name}CompletedEvent> CompletedEvents => Of<{Name}CompletedEvent>();
    public {Name}CompletedEvent? LastCompleted => Last<{Name}CompletedEvent>();
}
```

The typed alias adds zero boilerplate per module — just the convenience accessors you actually use in assertions.

**`TempDirectory`** — creates real temp dirs, auto-cleans on dispose:
```csharp
using var tmp = new TempDirectory("ExtractorTests");
var inputDir = tmp.CreateSubDir("input");
var outputDir = tmp.CreateSubDir("output");

TempDirectory.CreateFile(inputDir, "archive.7z", 4096);   // real file with random bytes
TempDirectory.CreateFile(inputDir, "game.iso", content);   // real file with specific content

// tmp.Root is the base path
// Everything deleted when tmp is disposed
```

### Test base class pattern

Each module has a test base in `Helpers/`:
```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Module.Core.Testing;

namespace Module.{Name}.Tests.Helpers;

public abstract class {Name}TestBase
{
    protected Fake{Name}Bridge Bridge { get; private set; } = null!;
    protected {Name}Service Service { get; private set; } = null!;

    private TempDirectory _tmp = null!;

    // Add module-specific directories as needed:
    // protected string InputDir { get; private set; } = null!;
    // protected string OutputDir { get; private set; } = null!;

    [TestInitialize]
    public void BaseSetup()
    {
        _tmp = new TempDirectory("{Name}Tests");
        // Create directories your module needs:
        // InputDir = _tmp.CreateSubDir("input");
        // OutputDir = _tmp.CreateSubDir("output");

        Bridge = new Fake{Name}Bridge();
        Service = new {Name}Service(Bridge, NullLogger<{Name}Service>.Instance);
        // Service.Configure(...) if needed
    }

    [TestCleanup]
    public void BaseCleanup() => _tmp.Dispose();

    protected static void CreateFile(string dir, string name, long sizeBytes = 1024)
        => TempDirectory.CreateFile(dir, name, sizeBytes);
}
```

### Test categories to cover

Every module should have tests for:

**1. Happy path** — the normal flow works end-to-end
```csharp
[TestMethod]
public async Task CopyFile_Success_ContentMatches()
{
    var content = new byte[4096];
    Random.Shared.NextBytes(content);
    File.WriteAllBytes(Path.Combine(SourceDir, "Game.iso"), content);

    await Service.CopyFileAsync("Game.iso");

    var destContent = File.ReadAllBytes(Path.Combine(TargetDir, "Game.iso"));
    CollectionAssert.AreEqual(content, destContent);  // byte-for-byte real verification
}
```

**2. Pre-flight failures** — bad input caught before work starts
```csharp
[TestMethod]
public async Task CopyFile_SourceMissing_NotifiesError()
{
    await Service.CopyFileAsync("DoesNotExist.iso");

    Assert.IsFalse(Bridge.LastCompleted!.Success);
    StringAssert.Contains(Bridge.LastCompleted.Error, "not found");
}
```

**3. Mid-operation failures** — things break during work
```csharp
[TestMethod]
public async Task CopyFile_TargetDirDisappears_HandledGracefully()
{
    CreateFile(SourceDir, "Game.iso", 1024);
    var tempTarget = Path.Combine(Path.GetTempPath(), "vanish_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempTarget);
    var svc = CreateService(syncPath: tempTarget);

    Directory.Delete(tempTarget, true);  // simulate disconnect

    await svc.CopyFileAsync("Game.iso");

    Assert.IsFalse(Bridge.LastCompleted!.Success);  // no crash, clean error
}
```

**4. Edge cases** — zero-byte files, special characters, case sensitivity
```csharp
[TestMethod]
public void Compare_CaseInsensitiveMatching()
{
    CreateFile(SourceDir, "Game.ISO", 1024);
    CreateFile(TargetDir, "game.iso", 1024);

    var result = Service.Compare();

    Assert.AreEqual(0, result.New.Count);   // matched despite case difference
    Assert.AreEqual(1, result.Synced.Count);
}
```

**5. State safety** — no leaks between operations
```csharp
[TestMethod]
public async Task AfterFailure_NextOperationWorks()
{
    await Service.CopyFileAsync("Ghost.iso");           // fails
    Assert.IsFalse(Service.IsCopying);                  // state reset

    CreateFile(SourceDir, "Real.iso", 1024);
    await Service.CopyFileAsync("Real.iso");            // succeeds
    Assert.IsTrue(Bridge.LastCompleted!.Success);       // clean
}
```

**6. Bridge events** — correct events emitted at correct times
```csharp
[TestMethod]
public async Task CopyFile_Success_EmitsFinalProgress100()
{
    CreateFile(SourceDir, "Game.iso", 4096);

    await Service.CopyFileAsync("Game.iso");

    var last = Bridge.ProgressEvents[^1];
    Assert.AreEqual(100.0, last.Percent);
    Assert.AreEqual(last.Total, last.Copied);
}
```

**7. Cancellation** — clean abort, no partial artifacts
```csharp
[TestMethod]
public async Task CopyFile_Cancelled_CleansUpPartialFile()
{
    CreateFile(SourceDir, "Big.iso", 1024 * 1024 * 5);
    using var cts = new CancellationTokenSource();
    cts.Cancel();  // pre-cancel for deterministic test

    await Service.CopyFileAsync("Big.iso", cts.Token);

    Assert.IsFalse(File.Exists(Path.Combine(TargetDir, "Big.iso")));  // no partial
    StringAssert.Contains(Bridge.LastCompleted!.Error, "Cancelled");
}
```

### Assertions: MSTest native only

Use `Assert.*` and `CollectionAssert.*` — no third-party assertion libraries.

| Pattern | Method |
|---------|--------|
| Value equality | `Assert.AreEqual(expected, actual)` |
| Boolean | `Assert.IsTrue(cond)` / `Assert.IsFalse(cond)` |
| Null | `Assert.IsNull(obj)` / `Assert.IsNotNull(obj)` |
| String contains | `StringAssert.Contains(str, substring)` |
| Collection equality | `CollectionAssert.AreEqual(expected, actual)` |
| File exists | `Assert.IsTrue(File.Exists(path))` |

---

## Dependency rules

```
Module.Core              no dependencies
Module.Core.Testing      Module.Core only
Module.{Name}            Module.Core + logging abstractions
Module.{Name}.Tests      Module.{Name} + Module.Core.Testing + MSTest
VimmsDownloader          Module.{Name} + ASP.NET + SignalR
```

A module must NEVER reference:
- VimmsDownloader (the host)
- ASP.NET / SignalR / any web framework
- Other modules (unless there's a real dependency)
- IConfiguration (use a Configure() method instead)

---

## Checklist for migrating an existing feature

- [ ] Create `Modules/Module.{Name}/` and `Module.{Name}.Tests/`
- [ ] Define bridge events in `Bridge/{Name}BridgeEvents.cs`
- [ ] Define bridge interface in `Bridge/I{Name}Bridge.cs`
- [ ] Move service logic, remove all ASP.NET/SignalR imports
- [ ] Replace direct hub calls with `_bridge.SendAsync(new Event(...))`
- [ ] Replace IConfiguration reads with a `Configure()` method
- [ ] Create `SignalR{Name}Bridge` in the host
- [ ] Update DI registration in `Program.cs`
- [ ] Register event types in `AppJsonContext.cs`
- [ ] Move/update endpoints to call the module service
- [ ] Create test helpers:
  - [ ] `Fake{Name}Bridge` — typed alias over `FakeBridge<{Name}Event>`
  - [ ] `{Name}TestBase` — base class using `TempDirectory` for real I/O
- [ ] Write tests covering: happy path, pre-flight failures, mid-operation failures, edge cases, state safety, bridge events, cancellation
- [ ] Add both projects to `VimmsDownloader.slnx`
- [ ] `dotnet build && dotnet test`
- [ ] Delete old files from VimmsDownloader

---

## Reference: Module.Sync

| What                    | File                                         |
|-------------------------|----------------------------------------------|
| Bridge interface        | `Module.Sync/Bridge/ISyncBridge.cs`          |
| Bridge events           | `Module.Sync/Bridge/SyncBridgeEvents.cs`     |
| Service                 | `Module.Sync/SyncService.cs`                 |
| Models                  | `Module.Sync/SyncModels.cs`                  |
| Host bridge (SignalR)   | `VimmsDownloader/SignalRSyncNotifier.cs`      |
| Host endpoints          | `VimmsDownloader/Endpoints/SyncEndpoints.cs`  |
| Host DI                 | `VimmsDownloader/Program.cs` (lines 10-11)    |
| Test fake bridge        | `Module.Sync.Tests/Helpers/FakeSyncBridge.cs` |
| Test base               | `Module.Sync.Tests/Helpers/SyncTestBase.cs`   |
| 67 integration tests    | `Module.Sync.Tests/*.cs`                      |
