using Module.Download;

[TestClass]
public class VaultPageParserEdgeCaseTests
{
    // --- MediaId edge cases ---

    [TestMethod]
    public void ExtractMediaId_WhitespaceInAttributes()
    {
        var html = """<input type="hidden"  name="mediaId"   value="99999"  >""";
        Assert.AreEqual("99999", VaultPageParser.ExtractMediaId(html));
    }

    [TestMethod]
    public void ExtractMediaId_MixedCase()
    {
        var html = """<input type="hidden" NAME="mediaId" VALUE="55555">""";
        Assert.AreEqual("55555", VaultPageParser.ExtractMediaId(html));
    }

    [TestMethod]
    public void ExtractMediaId_MultipleInputs_GetsFirst()
    {
        var html = """
            <input type="hidden" name="other" value="xxx">
            <input type="hidden" name="mediaId" value="11111">
            <input type="hidden" name="mediaId" value="22222">
            """;
        Assert.AreEqual("11111", VaultPageParser.ExtractMediaId(html));
    }

    [TestMethod]
    public void ExtractMediaId_EmptyValue_NotMatched()
    {
        var html = """<input type="hidden" name="mediaId" value="">""";
        Assert.IsNull(VaultPageParser.ExtractMediaId(html));
    }

    // --- Title edge cases ---

    [TestMethod]
    public void Parse_TitleWithHtmlEntities()
    {
        var html = """
            <title>The Vault: Assassin&#39;s Creed (PlayStation 3)</title>
            <input type="hidden" name="mediaId" value="100">
            <form id="dl_form" action="https://dl3.vimm.net/">
            """;
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/1", 0);
        Assert.IsNotNull(result);
        Assert.AreEqual("Assassin\u0027s Creed (PlayStation 3)", result.Title);
    }

    [TestMethod]
    public void Parse_TitleWithAmpersand()
    {
        var html = """
            <title>The Vault: Ratchet &amp; Clank (PlayStation 3)</title>
            <input type="hidden" name="mediaId" value="100">
            <form id="dl_form" action="https://dl3.vimm.net/">
            """;
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/1", 0);
        Assert.IsNotNull(result);
        StringAssert.Contains(result.Title, "Ratchet & Clank");
    }

    [TestMethod]
    public void Parse_TitleWithoutVaultPrefix()
    {
        var html = """
            <title>Some Other Title</title>
            <input type="hidden" name="mediaId" value="100">
            <form id="dl_form" action="https://dl3.vimm.net/">
            """;
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/1", 0);
        Assert.IsNotNull(result);
        Assert.AreEqual("Some Other Title", result.Title);
    }

    // --- DL server resolution edge cases ---

    [TestMethod]
    public void ResolveDlServer_HttpUpgradedToHttps()
    {
        var html = """<form id="dl_form" action="http://dl3.vimm.net/">""";
        var server = VaultPageParser.ResolveDlServer(html, "https://vimm.net/vault/1");
        Assert.IsTrue(server.StartsWith("https://"), $"Expected https, got: {server}");
    }

    [TestMethod]
    public void ResolveDlServer_TrailingSlashConsistency()
    {
        var html = """<form id="dl_form" action="https://dl3.vimm.net">""";
        var server = VaultPageParser.ResolveDlServer(html, "https://vimm.net/vault/1");
        Assert.IsTrue(server.EndsWith("/"), "Server URL should end with /");
    }

    [TestMethod]
    public void ResolveDlServer_MultipleMatches_GetsFormAction()
    {
        var html = """
            <form id="dl_form" action="https://dl5.vimm.net/">
            <script>.action = "https://dl2.vimm.net/"</script>
            """;
        var server = VaultPageParser.ResolveDlServer(html, "https://vimm.net/vault/1");
        StringAssert.Contains(server, "dl5"); // form action takes priority
    }

    [TestMethod]
    public void ResolveDlServer_RelativeUrl()
    {
        var html = """<form id="dl_form" action="/download/">""";
        var server = VaultPageParser.ResolveDlServer(html, "https://vimm.net/vault/1");
        Assert.IsTrue(server.StartsWith("https://"), $"Should resolve relative URL: {server}");
    }

    // --- Format parameter in download URL ---

    [TestMethod]
    public void Parse_Format0_NoAltParam()
    {
        var html = """
            <title>Game</title>
            <input type="hidden" name="mediaId" value="123">
            <form id="dl_form" action="https://dl3.vimm.net/">
            """;
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/1", 0);
        Assert.IsNotNull(result);
        Assert.IsFalse(result.DownloadUrl.Contains("alt="));
    }

    [TestMethod]
    public void Parse_Format2_NoOptionsOnPage_FallsBackToDefault()
    {
        var html = """
            <title>Game</title>
            <input type="hidden" name="mediaId" value="123">
            <form id="dl_form" action="https://dl3.vimm.net/">
            """;
        var result = VaultPageParser.Parse(html, "https://vimm.net/vault/1", 2);
        Assert.IsNotNull(result);
        // No format options on page -> falls back to 0 (default)
        Assert.AreEqual(0, result.ResolvedFormat);
        Assert.IsNotNull(result.FormatNote);
        Assert.IsFalse(result.DownloadUrl.Contains("alt="));
    }

    // --- Malformed HTML ---

    [TestMethod]
    public void Parse_EmptyHtml_ReturnsNull()
    {
        Assert.IsNull(VaultPageParser.Parse("", "https://vimm.net/vault/1", 0));
    }

    [TestMethod]
    public void Parse_GarbageHtml_ReturnsNull()
    {
        Assert.IsNull(VaultPageParser.Parse("@#$%^&*()", "https://vimm.net/vault/1", 0));
    }

    [TestMethod]
    public void ResolveDlServer_EmptyHtml_ReturnsFallback()
    {
        var server = VaultPageParser.ResolveDlServer("", "https://vimm.net/vault/1");
        StringAssert.StartsWith(server, "https://dl3.vimm.net");
    }
}
