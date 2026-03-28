using Module.Download;

[TestClass]
public class VaultPageParserTests
{
    [TestMethod]
    public void ExtractMediaId_StandardFormat()
    {
        var html = """<input type="hidden" name="mediaId" value="83789">""";
        Assert.AreEqual("83789", VaultPageParser.ExtractMediaId(html));
    }

    [TestMethod]
    public void ExtractMediaId_ReversedAttributes()
    {
        var html = """<input type="hidden" value="12345" name="mediaId">""";
        Assert.AreEqual("12345", VaultPageParser.ExtractMediaId(html));
    }

    [TestMethod]
    public void ExtractMediaId_NotFound_ReturnsNull()
    {
        var html = "<html><body>No media id here</body></html>";
        Assert.IsNull(VaultPageParser.ExtractMediaId(html));
    }

    [TestMethod]
    public void Parse_FullPage_ExtractsAll()
    {
        var html = """
            <title>The Vault: God of War III (PlayStation 3)</title>
            <input type="hidden" name="mediaId" value="83789">
            <form id="dl_form" action="https://dl3.vimm.net/">
            """;
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/84578", 0);

        Assert.IsNotNull(result);
        Assert.AreEqual("83789", result.MediaId);
        Assert.AreEqual("God of War III (PlayStation 3)", result.Title);
        StringAssert.Contains(result.DownloadUrl, "mediaId=83789");
    }

    [TestMethod]
    public void Parse_AltFormat_IncludesAltParam()
    {
        var html = """
            <title>The Vault: Game</title>
            <input type="hidden" name="mediaId" value="100">
            <form id="dl_form" action="https://dl3.vimm.net/">
            <option value="0" title="7z">7z Archive</option>
            <option value="1" title="dec.iso">.dec.iso</option>
            """;
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/1", 1);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.ResolvedFormat);
        Assert.IsNull(result.FormatNote);
        StringAssert.Contains(result.DownloadUrl, "alt=1");
    }

    [TestMethod]
    public void Parse_NoMediaId_ReturnsNull()
    {
        var html = "<title>Page</title><body>Nothing</body>";
        Assert.IsNull(VaultPageParser.Parse(html, "https://vimm.net/vault/1", 0));
    }

    [TestMethod]
    public void ResolveDlServer_FormAction()
    {
        var html = """<form id="dl_form" action="https://dl5.vimm.net/">""";
        var server = VaultPageParser.ResolveDlServer(html, "https://vimm.net/vault/1");
        StringAssert.StartsWith(server, "https://dl5.vimm.net");
    }

    [TestMethod]
    public void ResolveDlServer_JsAction()
    {
        var html = """.action = "https://dl2.vimm.net/" """;
        var server = VaultPageParser.ResolveDlServer(html, "https://vimm.net/vault/1");
        StringAssert.StartsWith(server, "https://dl2.vimm.net");
    }

    [TestMethod]
    public void ResolveDlServer_Fallback()
    {
        var html = "<body>nothing useful</body>";
        var server = VaultPageParser.ResolveDlServer(html, "https://vimm.net/vault/1");
        StringAssert.StartsWith(server, "https://dl3.vimm.net");
    }

    [TestMethod]
    public void ResolveDlServer_ProtocolRelative()
    {
        var html = """//dl4.vimm.net/""";
        var server = VaultPageParser.ResolveDlServer(html, "https://vimm.net/vault/1");
        StringAssert.StartsWith(server, "https://dl4.vimm.net");
    }

    // --- Format resolution ---

    [TestMethod]
    public void ResolveFormat_PreferredAvailable_UsesIt()
    {
        var available = new HashSet<int> { 0, 1 };
        var (format, note) = VaultPageParser.ResolveFormat(1, available);
        Assert.AreEqual(1, format);
        Assert.IsNull(note);
    }

    [TestMethod]
    public void ResolveFormat_PreferredNotAvailable_FallsBackToJbFolder()
    {
        var available = new HashSet<int> { 0 };
        var (format, note) = VaultPageParser.ResolveFormat(1, available);
        Assert.AreEqual(0, format);
        Assert.IsNotNull(note);
        StringAssert.Contains(note, "not available");
        StringAssert.Contains(note, "JB Folder");
    }

    [TestMethod]
    public void ResolveFormat_NoFormatsOnPage_DefaultOnly()
    {
        var available = new HashSet<int>();
        var (format, note) = VaultPageParser.ResolveFormat(0, available);
        Assert.AreEqual(0, format);
        Assert.IsNull(note);
    }

    [TestMethod]
    public void ResolveFormat_NoFormatsOnPage_PreferredUnavailable_FallsBack()
    {
        var available = new HashSet<int>();
        var (format, note) = VaultPageParser.ResolveFormat(1, available);
        Assert.AreEqual(0, format);
        Assert.IsNotNull(note);
        StringAssert.Contains(note, "not available");
    }

    [TestMethod]
    public void ResolveFormat_NeitherPreferredNorZero_UsesFirst()
    {
        var available = new HashSet<int> { 2, 3 };
        var (format, note) = VaultPageParser.ResolveFormat(1, available);
        Assert.IsTrue(available.Contains(format));
        Assert.IsNotNull(note);
        StringAssert.Contains(note, "not available");
    }

    [TestMethod]
    public void ResolveFormat_ZeroPreferred_ZeroAvailable()
    {
        var available = new HashSet<int> { 0, 1 };
        var (format, note) = VaultPageParser.ResolveFormat(0, available);
        Assert.AreEqual(0, format);
        Assert.IsNull(note);
    }

    [TestMethod]
    public void ExtractAvailableFormats_ParsesOptionTags()
    {
        var html = """
            <option value="0" title="7z">7z Archive</option>
            <option value="1" title="dec.iso">.dec.iso</option>
            """;
        var formats = VaultPageParser.ExtractAvailableFormats(html);
        Assert.AreEqual(2, formats.Count);
        Assert.IsTrue(formats.Contains(0));
        Assert.IsTrue(formats.Contains(1));
    }

    [TestMethod]
    public void ExtractAvailableFormats_NoOptions_EmptySet()
    {
        var html = "<body>No format options</body>";
        var formats = VaultPageParser.ExtractAvailableFormats(html);
        Assert.AreEqual(0, formats.Count);
    }

    [TestMethod]
    public void Parse_FormatFallback_EmittedInResult()
    {
        var html = """
            <title>The Vault: Game</title>
            <input type="hidden" name="mediaId" value="100">
            <form id="dl_form" action="https://dl3.vimm.net/">
            <option value="0" title="7z">7z Archive</option>
            """;
        // Request format 1 but only 0 is available
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/1", 1);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ResolvedFormat);
        Assert.IsNotNull(result.FormatNote);
        StringAssert.Contains(result.FormatNote, "not available");
        // URL should NOT contain alt= since resolved to format 0
        Assert.IsFalse(result.DownloadUrl.Contains("alt="));
    }

    [TestMethod]
    public void Parse_FormatAvailable_NoNote()
    {
        var html = """
            <title>The Vault: Game</title>
            <input type="hidden" name="mediaId" value="100">
            <form id="dl_form" action="https://dl3.vimm.net/">
            <option value="0" title="7z">7z Archive</option>
            <option value="1" title="dec.iso">.dec.iso</option>
            """;
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/1", 1);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.ResolvedFormat);
        Assert.IsNull(result.FormatNote);
        StringAssert.Contains(result.DownloadUrl, "alt=1");
    }
}
