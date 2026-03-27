using Module.Ps3Iso.Tests.Helpers;

[TestClass]
public class ParamSfoEdgeCaseTests : Ps3IsoTestBase
{
    // --- Corrupt SFO binaries ---

    [TestMethod]
    public void Parse_ValidMagicButZeroEntries_ReturnsNull()
    {
        // Valid header but numEntries = 0
        var data = new byte[0x14];
        data[0] = 0x00; data[1] = 0x50; data[2] = 0x53; data[3] = 0x46;
        BitConverter.GetBytes(0x14).CopyTo(data, 8);  // keyTableStart
        BitConverter.GetBytes(0x14).CopyTo(data, 12); // dataTableStart
        BitConverter.GetBytes(0).CopyTo(data, 16);    // numEntries = 0

        var path = Path.Combine(Tmp.Root, "zero_entries.sfo");
        File.WriteAllBytes(path, data);

        Assert.IsNull(ParamSfo.Parse(path));
    }

    [TestMethod]
    public void Parse_ValidMagicButTruncatedIndexTable_ReturnsNull()
    {
        // Header says 5 entries but file is too small to hold them
        var data = new byte[0x20]; // only enough for ~0.3 index entries
        data[0] = 0x00; data[1] = 0x50; data[2] = 0x53; data[3] = 0x46;
        BitConverter.GetBytes(0x100).CopyTo(data, 8);
        BitConverter.GetBytes(0x200).CopyTo(data, 12);
        BitConverter.GetBytes(5).CopyTo(data, 16);

        var path = Path.Combine(Tmp.Root, "trunc_index.sfo");
        File.WriteAllBytes(path, data);

        // Should not crash — returns null or partial result
        var result = ParamSfo.Parse(path);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Parse_OnlyTitleNoTitleId_ReturnsNull()
    {
        // SFO with only TITLE, missing TITLE_ID — should return null
        var data = BuildSingleKeySfo("TITLE", "Some Game");

        var path = Path.Combine(Tmp.Root, "no_titleid.sfo");
        File.WriteAllBytes(path, data);

        Assert.IsNull(ParamSfo.Parse(path));
    }

    [TestMethod]
    public void Parse_OnlyTitleIdNoTitle_ReturnsNull()
    {
        var data = BuildSingleKeySfo("TITLE_ID", "BCES00510");

        var path = Path.Combine(Tmp.Root, "no_title.sfo");
        File.WriteAllBytes(path, data);

        Assert.IsNull(ParamSfo.Parse(path));
    }

    [TestMethod]
    public void Parse_ExactlyHeaderSize_ReturnsNull()
    {
        // File is exactly 0x14 bytes — valid magic but no entries to parse
        var data = new byte[0x14];
        data[0] = 0x00; data[1] = 0x50; data[2] = 0x53; data[3] = 0x46;

        var path = Path.Combine(Tmp.Root, "minimal.sfo");
        File.WriteAllBytes(path, data);

        Assert.IsNull(ParamSfo.Parse(path));
    }

    [TestMethod]
    public void Parse_AllZerosBeyondMagic_ReturnsNull()
    {
        var data = new byte[256];
        data[0] = 0x00; data[1] = 0x50; data[2] = 0x53; data[3] = 0x46;

        var path = Path.Combine(Tmp.Root, "zeros.sfo");
        File.WriteAllBytes(path, data);

        // Should not crash
        var result = ParamSfo.Parse(path);
        // Result is either null or has empty values — no crash
    }

    // --- Title ID formatting ---

    [TestMethod]
    public void Parse_ShortTitleId_NoModification()
    {
        var sfoPath = Path.Combine(Tmp.Root, "short_id.sfo");
        File.WriteAllBytes(sfoPath, BuildParamSfo("Game", "ABC"));

        var result = ParamSfo.Parse(sfoPath);
        Assert.IsNotNull(result);
        Assert.AreEqual("ABC", result.TitleId);
    }

    [TestMethod]
    public void Parse_ExactlyFourCharTitleId_NoModification()
    {
        var sfoPath = Path.Combine(Tmp.Root, "four_id.sfo");
        File.WriteAllBytes(sfoPath, BuildParamSfo("Game", "BCES"));

        var result = ParamSfo.Parse(sfoPath);
        Assert.IsNotNull(result);
        Assert.AreEqual("BCES", result.TitleId);
    }

    [TestMethod]
    public void Parse_NineCharNoDash_InsertsDash()
    {
        var sfoPath = Path.Combine(Tmp.Root, "nine.sfo");
        File.WriteAllBytes(sfoPath, BuildParamSfo("Game", "BLES01807"));

        var result = ParamSfo.Parse(sfoPath);
        Assert.IsNotNull(result);
        Assert.AreEqual("BLES-01807", result.TitleId);
    }

    [TestMethod]
    public void Parse_NineCharWithDash_PreservesDash()
    {
        var sfoPath = Path.Combine(Tmp.Root, "dash.sfo");
        File.WriteAllBytes(sfoPath, BuildParamSfo("Game", "BLES-0180"));

        var result = ParamSfo.Parse(sfoPath);
        Assert.IsNotNull(result);
        Assert.AreEqual("BLES-0180", result.TitleId);
    }

    // --- Special characters in title ---

    [TestMethod]
    public void Parse_TitleWithRegisteredTrademark()
    {
        var sfoPath = Path.Combine(Tmp.Root, "tm.sfo");
        File.WriteAllBytes(sfoPath, BuildParamSfo("inFamous\u2122", "BCES00609"));

        var result = ParamSfo.Parse(sfoPath);
        Assert.IsNotNull(result);
        Assert.AreEqual("inFamous\u2122", result.Title);
    }

    [TestMethod]
    public void Parse_TitleWithJapanese()
    {
        var sfoPath = Path.Combine(Tmp.Root, "jp.sfo");
        File.WriteAllBytes(sfoPath, BuildParamSfo("\u30b9\u30b1\u30fc\u30c8\u0033", "BLES00760"));

        var result = ParamSfo.Parse(sfoPath);
        Assert.IsNotNull(result);
        Assert.AreEqual("\u30b9\u30b1\u30fc\u30c8\u0033", result.Title);
    }

    [TestMethod]
    public void Parse_TitleWithAmpersand()
    {
        var sfoPath = Path.Combine(Tmp.Root, "amp.sfo");
        File.WriteAllBytes(sfoPath, BuildParamSfo("Ratchet & Clank", "BCES00052"));

        var result = ParamSfo.Parse(sfoPath);
        Assert.IsNotNull(result);
        Assert.AreEqual("Ratchet & Clank", result.Title);
    }

    [TestMethod]
    public void Parse_VeryLongTitle_Preserved()
    {
        var longTitle = new string('A', 200);
        var sfoPath = Path.Combine(Tmp.Root, "long.sfo");
        File.WriteAllBytes(sfoPath, BuildParamSfo(longTitle, "BCES00001"));

        var result = ParamSfo.Parse(sfoPath);
        Assert.IsNotNull(result);
        Assert.AreEqual(longTitle, result.Title);
    }

    // --- Helper to build SFO with a single key ---

    private static byte[] BuildSingleKeySfo(string key, string value)
    {
        var headerSize = 0x14;
        var indexTableSize = 16; // 1 entry

        var keyBytesRaw = System.Text.Encoding.UTF8.GetBytes(key + "\0");
        while (keyBytesRaw.Length % 4 != 0)
            keyBytesRaw = [.. keyBytesRaw, 0];

        var valBytesRaw = System.Text.Encoding.UTF8.GetBytes(value + "\0");
        while (valBytesRaw.Length % 4 != 0)
            valBytesRaw = [.. valBytesRaw, 0];

        var keyTableStart = headerSize + indexTableSize;
        var dataTableStart = keyTableStart + keyBytesRaw.Length;
        var totalSize = dataTableStart + valBytesRaw.Length;

        var result = new byte[totalSize];
        result[0] = 0x00; result[1] = 0x50; result[2] = 0x53; result[3] = 0x46;
        BitConverter.GetBytes(0x00000101).CopyTo(result, 4);
        BitConverter.GetBytes(keyTableStart).CopyTo(result, 8);
        BitConverter.GetBytes(dataTableStart).CopyTo(result, 12);
        BitConverter.GetBytes(1).CopyTo(result, 16);

        // Index entry
        BitConverter.GetBytes((ushort)0).CopyTo(result, headerSize);
        BitConverter.GetBytes((ushort)0x0204).CopyTo(result, headerSize + 2);
        BitConverter.GetBytes(valBytesRaw.Length).CopyTo(result, headerSize + 4);
        BitConverter.GetBytes(valBytesRaw.Length).CopyTo(result, headerSize + 8);
        BitConverter.GetBytes(0).CopyTo(result, headerSize + 12);

        keyBytesRaw.CopyTo(result, keyTableStart);
        valBytesRaw.CopyTo(result, dataTableStart);

        return result;
    }
}
