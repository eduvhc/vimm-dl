using Microsoft.Extensions.Logging.Abstractions;
using Module.Core.Testing;

namespace Module.Ps3Iso.Tests.Helpers;

public abstract class Ps3IsoTestBase
{
    protected FakePs3IsoBridge Bridge { get; private set; } = null!;
    protected TempDirectory Tmp { get; private set; } = null!;

    [TestInitialize]
    public void BaseSetup()
    {
        Tmp = new TempDirectory("Ps3IsoTests");
        Bridge = new FakePs3IsoBridge();
    }

    [TestCleanup]
    public void BaseCleanup() => Tmp.Dispose();

    protected Ps3ConversionPipeline CreatePipeline()
    {
        return new Ps3ConversionPipeline(Bridge, NullLogger<Ps3ConversionPipeline>.Instance);
    }

    /// <summary>
    /// Creates a minimal valid PARAM.SFO binary with the given title and title ID.
    /// </summary>
    protected static byte[] BuildParamSfo(string title, string titleId)
    {
        // PARAM.SFO binary format: header + index table + key table + data table
        var keys = new[] { ("TITLE", title), ("TITLE_ID", titleId) };
        var numEntries = keys.Length;

        // Calculate offsets
        var indexTableSize = numEntries * 16;
        var headerSize = 0x14;

        // Build key table
        var keyBytes = new List<byte>();
        var keyOffsets = new List<int>();
        foreach (var (key, _) in keys)
        {
            keyOffsets.Add(keyBytes.Count);
            keyBytes.AddRange(System.Text.Encoding.UTF8.GetBytes(key));
            keyBytes.Add(0); // null terminator
        }
        // Align to 4 bytes
        while (keyBytes.Count % 4 != 0) keyBytes.Add(0);

        var keyTableStart = headerSize + indexTableSize;
        var dataTableStart = keyTableStart + keyBytes.Count;

        // Build data table
        var dataBytes = new List<byte>();
        var dataOffsets = new List<int>();
        var dataLens = new List<int>();
        foreach (var (_, value) in keys)
        {
            dataOffsets.Add(dataBytes.Count);
            var valBytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
            dataLens.Add(valBytes.Length);
            dataBytes.AddRange(valBytes);
            // Align to 4 bytes
            while (dataBytes.Count % 4 != 0) dataBytes.Add(0);
        }

        // Build full binary
        var totalSize = dataTableStart + dataBytes.Count;
        var result = new byte[totalSize];

        // Header: magic (00 50 53 46), version, key table offset, data table offset, num entries
        result[0] = 0x00; result[1] = 0x50; result[2] = 0x53; result[3] = 0x46; // magic
        BitConverter.GetBytes(0x00000101).CopyTo(result, 4); // version 1.1
        BitConverter.GetBytes(keyTableStart).CopyTo(result, 8);
        BitConverter.GetBytes(dataTableStart).CopyTo(result, 12);
        BitConverter.GetBytes(numEntries).CopyTo(result, 16);

        // Index table entries
        for (int i = 0; i < numEntries; i++)
        {
            var offset = headerSize + i * 16;
            BitConverter.GetBytes((ushort)keyOffsets[i]).CopyTo(result, offset);     // key offset
            BitConverter.GetBytes((ushort)0x0204).CopyTo(result, offset + 2);        // data format (UTF-8)
            BitConverter.GetBytes(dataLens[i]).CopyTo(result, offset + 4);           // data used len
            BitConverter.GetBytes(dataLens[i]).CopyTo(result, offset + 8);           // data max len
            BitConverter.GetBytes(dataOffsets[i]).CopyTo(result, offset + 12);       // data offset
        }

        // Key table
        keyBytes.ToArray().CopyTo(result, keyTableStart);

        // Data table
        dataBytes.ToArray().CopyTo(result, dataTableStart);

        return result;
    }

    /// <summary>Creates a realistic PS3 JB folder structure with PARAM.SFO.</summary>
    protected string CreateJbFolder(string parentDir, string title = "Test Game", string titleId = "BCES00001")
    {
        var jbDir = Path.Combine(parentDir, "GAMEFOLDER");
        var ps3GameDir = Path.Combine(jbDir, "PS3_GAME");
        Directory.CreateDirectory(ps3GameDir);
        File.WriteAllBytes(Path.Combine(ps3GameDir, "PARAM.SFO"), BuildParamSfo(title, titleId));
        return jbDir;
    }
}
