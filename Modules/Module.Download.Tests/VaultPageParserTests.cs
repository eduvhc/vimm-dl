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
            """;
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/1", 1);

        Assert.IsNotNull(result);
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
}
