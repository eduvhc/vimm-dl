using Module.Core.Testing;

namespace Module.Ps3IsoTools.Tests.Helpers;

public abstract class Ps3ToolsTestBase
{
    protected TempDirectory Tmp { get; private set; } = null!;

    [TestInitialize]
    public void BaseSetup() => Tmp = new TempDirectory("Ps3ToolsTests");

    [TestCleanup]
    public void BaseCleanup() => Tmp.Dispose();

    protected string CreateJbFolder(string parentDir, string title = "Test Game", string titleId = "BCES00001")
    {
        var jbDir = Path.Combine(parentDir, "GAMEFOLDER");
        var ps3GameDir = Path.Combine(jbDir, "PS3_GAME");
        Directory.CreateDirectory(ps3GameDir);
        File.WriteAllBytes(Path.Combine(ps3GameDir, "PARAM.SFO"), BuildParamSfo(title, titleId));
        return jbDir;
    }

    protected static byte[] BuildParamSfo(string title, string titleId)
    {
        var keys = new[] { ("TITLE", title), ("TITLE_ID", titleId) };
        var numEntries = keys.Length;
        var indexTableSize = numEntries * 16;
        var headerSize = 0x14;
        var keyBytes = new List<byte>();
        var keyOffsets = new List<int>();
        foreach (var (key, _) in keys)
        {
            keyOffsets.Add(keyBytes.Count);
            keyBytes.AddRange(System.Text.Encoding.UTF8.GetBytes(key));
            keyBytes.Add(0);
        }
        while (keyBytes.Count % 4 != 0) keyBytes.Add(0);
        var keyTableStart = headerSize + indexTableSize;
        var dataTableStart = keyTableStart + keyBytes.Count;
        var dataBytes = new List<byte>();
        var dataOffsets = new List<int>();
        var dataLens = new List<int>();
        foreach (var (_, value) in keys)
        {
            dataOffsets.Add(dataBytes.Count);
            var valBytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
            dataLens.Add(valBytes.Length);
            dataBytes.AddRange(valBytes);
            while (dataBytes.Count % 4 != 0) dataBytes.Add(0);
        }
        var totalSize = dataTableStart + dataBytes.Count;
        var result = new byte[totalSize];
        result[0] = 0x00; result[1] = 0x50; result[2] = 0x53; result[3] = 0x46;
        BitConverter.GetBytes(0x00000101).CopyTo(result, 4);
        BitConverter.GetBytes(keyTableStart).CopyTo(result, 8);
        BitConverter.GetBytes(dataTableStart).CopyTo(result, 12);
        BitConverter.GetBytes(numEntries).CopyTo(result, 16);
        for (int i = 0; i < numEntries; i++)
        {
            var offset = headerSize + i * 16;
            BitConverter.GetBytes((ushort)keyOffsets[i]).CopyTo(result, offset);
            BitConverter.GetBytes((ushort)0x0204).CopyTo(result, offset + 2);
            BitConverter.GetBytes(dataLens[i]).CopyTo(result, offset + 4);
            BitConverter.GetBytes(dataLens[i]).CopyTo(result, offset + 8);
            BitConverter.GetBytes(dataOffsets[i]).CopyTo(result, offset + 12);
        }
        keyBytes.ToArray().CopyTo(result, keyTableStart);
        dataBytes.ToArray().CopyTo(result, dataTableStart);
        return result;
    }
}
