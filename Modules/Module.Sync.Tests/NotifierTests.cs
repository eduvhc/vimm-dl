using Module.Sync.Tests.Helpers;
using Module.Sync.Bridge;

[TestClass]
public class BridgeTests
{
    [TestMethod]
    public async Task FakeBridge_CapturesProgress()
    {
        var bridge = new FakeSyncBridge();

        await bridge.SendAsync(new SyncProgressEvent("f.iso", 50.0, 100, 200));

        Assert.AreEqual(1, bridge.ProgressEvents.Count);
        Assert.AreEqual("f.iso", bridge.ProgressEvents[0].Filename);
        Assert.AreEqual(50.0, bridge.ProgressEvents[0].Percent);
    }

    [TestMethod]
    public async Task FakeBridge_CapturesCompleted()
    {
        var bridge = new FakeSyncBridge();

        await bridge.SendAsync(new SyncCompletedEvent("f.iso", true, null));

        Assert.AreEqual(1, bridge.CompletedEvents.Count);
        Assert.IsTrue(bridge.LastCompleted!.Success);
    }

    [TestMethod]
    public async Task FakeBridge_ThreadSafe_CapturesAll()
    {
        var bridge = new FakeSyncBridge();

        var tasks = Enumerable.Range(0, 50).Select(i =>
            bridge.SendAsync(new SyncCompletedEvent($"File{i}.iso", true, null)));

        await Task.WhenAll(tasks);

        Assert.AreEqual(50, bridge.CompletedEvents.Count);
    }

    [TestMethod]
    public void FakeBridge_Clear_ResetsAll()
    {
        var bridge = new FakeSyncBridge();
        bridge.SendAsync(new SyncProgressEvent("f.iso", 50, 100, 200));
        bridge.SendAsync(new SyncCompletedEvent("f.iso", true, null));

        bridge.Clear();

        Assert.AreEqual(0, bridge.AllEvents.Count);
        Assert.IsNull(bridge.LastCompleted);
    }

    [TestMethod]
    public async Task FakeBridge_AllEvents_MixedTypes()
    {
        var bridge = new FakeSyncBridge();

        await bridge.SendAsync(new SyncProgressEvent("a.iso", 50, 100, 200));
        await bridge.SendAsync(new SyncCompletedEvent("a.iso", true, null));
        await bridge.SendAsync(new SyncProgressEvent("b.iso", 25, 50, 200));

        Assert.AreEqual(3, bridge.AllEvents.Count);
        Assert.AreEqual(2, bridge.ProgressEvents.Count);
        Assert.AreEqual(1, bridge.CompletedEvents.Count);
    }
}
