using Microsoft.Extensions.Logging;
using Module.Core.Pipeline;
using Module.Ps3IsoTools;
using Module.Ps3Pipeline.Bridge;

namespace Module.Ps3Pipeline;

/// <summary>
/// Facade over both PS3 pipelines. Implements IPipeline for generic host access.
/// </summary>
public class Ps3ConversionPipeline : IPipeline
{
    private readonly PipelineState _state;

    public Ps3JbFolderPipeline JbFolder { get; }
    public Ps3DecIsoPipeline DecIso { get; }

    public Ps3ConversionPipeline(IPs3PipelineBridge bridge, ILogger<Ps3ConversionPipeline> log)
    {
        _state = new PipelineState(bridge, log);
        DecIso = new Ps3DecIsoPipeline(_state);
        JbFolder = new Ps3JbFolderPipeline(_state, DecIso);
    }

    // JB Folder
    public void Configure(int maxParallelism) => JbFolder.Configure(maxParallelism);
    public void CleanupOrphans(string downloadBasePath) => JbFolder.CleanupOrphans(downloadBasePath);
    public bool Enqueue(string zipPath, string completedDir, string tempBaseDir, bool force = false)
        => JbFolder.Enqueue(zipPath, completedDir, tempBaseDir, force);

    // Dec ISO
    public Task RenameDecIsoAsync(string filePath, string? serial = null, IsoRenameOptions? renameOptions = null)
        => DecIso.RenameDecIsoAsync(filePath, serial, renameOptions);
    public Task ExtractAndRenameDecIsoAsync(string archivePath, string completedDir, string tempBaseDir,
        string? serial = null, IsoRenameOptions? renameOptions = null)
        => DecIso.ExtractAndRenameDecIsoAsync(archivePath, completedDir, tempBaseDir, serial, renameOptions);

    // IPipeline
    public List<PipelineStatusEvent> GetStatuses() => _state.GetStatuses();
    public bool Abort(string itemName) => _state.Abort(itemName);
    public bool IsConverted(string itemName) => _state.IsConverted(itemName);
    public void MarkConverted(string itemName) => _state.MarkConverted(itemName);
}
