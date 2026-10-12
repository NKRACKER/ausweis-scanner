using System;
using AusweisScanner.Core;
using Xunit;

namespace AusweisScanner.Tests;

public class UpdateTests
{
    static string Release(string tag, bool draft = false, bool pre = false, bool withSums = true, bool withX86 = true, string host = "https://github.com") => $@"{{
      ""tag_name"": ""{tag}"", ""html_url"": ""{host}/o/r/releases/tag/{tag}"", ""draft"": {(draft ? "true" : "false")}, ""prerelease"": {(pre ? "true" : "false")}, ""body"": ""Neu: Dinge"",
      ""assets"": [
        {{ ""name"": ""Ausweis-Scanner-x64.exe"", ""size"": 806400, ""browser_download_url"": ""{host}/o/r/releases/download/{tag}/Ausweis-Scanner-x64.exe"" }},
        {(withX86 ? $@"{{ ""name"": ""Ausweis-Scanner-x86.exe"", ""size"": 807424, ""browser_download_url"": ""{host}/o/r/releases/download/{tag}/Ausweis-Scanner-x86.exe"" }}," : "")}
        {(withSums ? $@"{{ ""name"": ""SHA256SUMS.txt"", ""size"": 279, ""browser_download_url"": ""{host}/o/r/releases/download/{tag}/SHA256SUMS.txt"" }}," : "")}
        {{ ""name"": ""Test-Scan.png"", ""size"": 1, ""browser_download_url"": ""{host}/x.png"" }}
      ] }}";

    [Theory]
    [InlineData("v0.2.0", "0.1.0.0", true)]
    [InlineData("v0.1.0", "0.1.0.0", false)]     // gleiche Version
    [InlineData("v0.1.0", "0.2.0.0", false)]     // installierte ist neuer
    [InlineData("v0.10.0", "0.9.0.0", true)]     // numerisch, nicht alphabetisch
    [InlineData("1.0", "0.9.9.0", true)]         // ohne v und ohne dritte Zahl
    public void NewerOnly(string tag, string current, bool expectUpdate)
        => Assert.Equal(expectUpdate, UpdateLogic.FromReleaseJson(Release(tag), Version.Parse(current), true) != null);

    [Fact]
    public void PicksAssetByBitness()
    {
        var a = UpdateLogic.FromReleaseJson(Release("v0.2.0"), new Version(0, 1, 0), true)!;
        var b = UpdateLogic.FromReleaseJson(Release("v0.2.0"), new Version(0, 1, 0), false)!;
        Assert.Equal("Ausweis-Scanner-x64.exe", a.AssetName); Assert.EndsWith("/v0.2.0/Ausweis-Scanner-x64.exe", a.AssetUrl); Assert.EndsWith("SHA256SUMS.txt", a.SumsUrl);
        Assert.Equal("Ausweis-Scanner-x86.exe", b.AssetName); Assert.Equal(807424, b.Size); Assert.Equal(new Version(0, 2, 0), a.Version); Assert.Equal("Neu: Dinge", a.Notes);
    }

    [Fact] public void IgnoresDraftsAndPrereleases()
    {
        Assert.Null(UpdateLogic.FromReleaseJson(Release("v0.2.0", draft: true), new Version(0, 1, 0), true));
        Assert.Null(UpdateLogic.FromReleaseJson(Release("v0.2.0", pre: true), new Version(0, 1, 0), true));
    }

    [Fact] public void NoUpdateWithoutChecksumOrMatchingAsset()
    {
        Assert.Null(UpdateLogic.FromReleaseJson(Release("v0.2.0", withSums: false), new Version(0, 1, 0), true));      // nie ungeprüft installieren
        Assert.Null(UpdateLogic.FromReleaseJson(Release("v0.2.0", withX86: false), new Version(0, 1, 0), false));
    }

    [Fact] public void RejectsNonHttpsDownloadUrls() => Assert.Null(UpdateLogic.FromReleaseJson(Release("v0.2.0", host: "http://evil.example"), new Version(0, 1, 0), true));

    [Theory] [InlineData("{")] [InlineData("[]")] [InlineData("{\"message\":\"Not Found\"}")] [InlineData("")]
    public void GarbageIsNoUpdate(string json)
    {
        try { Assert.Null(UpdateLogic.FromReleaseJson(json, new Version(0, 1, 0), true)); } catch (FormatException) { /* defekte Antwort → Aufrufer behandelt als „keine Info“ */ }
    }

    [Fact]
    public void ParsesSha256sumLines()
    {
        var h = new string('A', 64); var h2 = new string('b', 64);
        var sums = $"{h}  Ausweis-Scanner-x64.exe\r\n{h2} *Ausweis-Scanner-x86.exe\n\nmüll\n";
        Assert.Equal(h.ToLowerInvariant(), UpdateLogic.ExpectedSha256(sums, "Ausweis-Scanner-x64.exe"));
        Assert.Equal(h2, UpdateLogic.ExpectedSha256(sums, "Ausweis-Scanner-x86.exe"));
        Assert.Null(UpdateLogic.ExpectedSha256(sums, "andere.exe")); Assert.Null(UpdateLogic.ExpectedSha256("", "x"));
    }
}
