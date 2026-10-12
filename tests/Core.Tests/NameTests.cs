using System.Collections.Generic;
using System.Linq;
using AusweisScanner.Core;
using Xunit;

namespace AusweisScanner.Tests;

public class NameParserTests
{
    [Theory]
    [InlineData("MUSTERMANN, MAX", "Mustermann", "Max")]
    [InlineData("Mustermann, Max", "Mustermann", "Max")]
    [InlineData("Max Mustermann", "Mustermann", "Max")]
    [InlineData("MAX MUSTERMANN", "Mustermann", "Max")]
    [InlineData("Name: Mustermann, Max", "Mustermann", "Max")]
    [InlineData("Anna van der Berg", "van der Berg", "Anna")]
    [InlineData("MÜLLER Anna-Lena", "Müller", "Anna-Lena")]
    [InlineData("Jean-Luc Meyer-Lüdenscheidt", "Meyer-Lüdenscheidt", "Jean-Luc")]
    [InlineData("O'NEIL, Sean", "O'Neil", "Sean")]
    [InlineData("  Schmidt ,  Lena  Sophie ", "Schmidt", "Lena Sophie")]
    [InlineData("SCHM1DT, Lena", "Schmidt", "Lena")]   // verlesene Ziffer in Großschreibung: 1 → I
    [InlineData("M0ller, Hans", "Moller", "Hans")]      // 0 → o in gemischter Schreibweise
    public void Parses(string raw, string last, string first)
    {
        var n = NameParser.Parse(raw);
        Assert.Equal(last, n.Last); Assert.Equal(first, n.First);
    }

    [Fact] public void SingleWordIsLastNameOnly() { var n = NameParser.Parse("Mustermann"); Assert.Equal("Mustermann", n.Last); Assert.Equal("", n.First); }
    [Fact] public void ExplicitFormatsOverrideGuess()
    {
        var a = NameParser.Parse("Meier Hans", NameFormat.LastFirst); Assert.Equal("Meier", a.Last); Assert.Equal("Hans", a.First);
        var b = NameParser.Parse("Meier Hans", NameFormat.FirstLast); Assert.Equal("Hans", b.Last); Assert.Equal("Meier", b.First);
    }
    [Fact] public void ProfileStripPrefix() { var n = NameParser.Parse("Schüler/in: Anna Meier", NameFormat.Auto, new[] { "Schüler/in:" }); Assert.Equal("Meier", n.Last); Assert.Equal("Anna", n.First); }

    [Fact] public void PlausibilityRanksNamesAboveHeadlines()
    {
        Assert.True(NameParser.Plausibility(NameParser.Parse("Max Mustermann")) > 0.7);
        Assert.True(NameParser.Plausibility(NameParser.Parse("Schülerausweis Gymnasium")) < 0.3);
        Assert.True(NameParser.Plausibility(NameParser.Parse("Humboldt Schule"), new[] { "Humboldt" }) < 0.3);
        Assert.Equal(0, NameParser.Plausibility(new ParsedName()));
    }
}

public class FileNamerTests
{
    [Fact] public void DefaultTemplate() => Assert.Equal("Müller_Anna", FileNamer.BaseName(new ParsedName("Müller", "Anna")));
    [Fact] public void MissingFirstNameLeavesNoDanglingSeparator() => Assert.Equal("Müller", FileNamer.BaseName(new ParsedName("Müller", "")));
    [Fact] public void UnknownFallsBack() => Assert.Equal("Unbekannt", FileNamer.BaseName(new ParsedName()));
    [Fact] public void RemovesIllegalChars() => Assert.Equal("Oneil_Sean", FileNamer.BaseName(new ParsedName("O/neil?", "Sean:*")));
    [Fact] public void TransliteratesOnRequest() => Assert.Equal("Mueller_José Strauss", FileNamer.BaseName(new ParsedName("Müller", "José Strauß"), new NamingOptions { TransliterateUmlauts = true }));
    [Fact] public void ReservedDeviceNameIsPrefixed() => Assert.Equal("_CON", FileNamer.BaseName(new ParsedName("CON", "")));
    [Fact] public void CustomTemplateWithClass() => Assert.Equal("7b_Müller_Anna", FileNamer.BaseName(new ParsedName("Müller", "Anna"), new NamingOptions { Template = "{Klasse}_{Nachname}_{Vorname}" }, new Dictionary<string, string> { ["Klasse"] = "7b" }));
    [Fact] public void LongNamesAreCapped() => Assert.True(FileNamer.BaseName(new ParsedName(new string('a', 300), "x")).Length <= 120);

    [Fact]
    public void UniqueNeverOverwrites()
    {
        var existing = new HashSet<string> { "a.jpg", "a_2.jpg" }; var taken = new HashSet<string>();
        Assert.Equal("a_3.jpg", FileNamer.Unique("a", ".jpg", existing.Contains, taken));
        Assert.Equal("a_4.jpg", FileNamer.Unique("a", ".jpg", existing.Contains, taken));   // zweite Karte mit gleichem Namen im selben Lauf
        Assert.Equal("b.jpg", FileNamer.Unique("b", ".jpg", existing.Contains, taken));
    }
}

public class MiniJsonAndProfileTests
{
    [Fact]
    public void JsonRoundtrip()
    {
        var o = new Dictionary<string, object?> { ["a"] = "ä\"\\\n", ["b"] = 1.5, ["c"] = true, ["d"] = null, ["e"] = new List<object?> { 1, "x", new Dictionary<string, object?> { ["k"] = false } } };
        var back = (IDictionary<string, object?>)MiniJson.Parse(MiniJson.Write(o))!;
        Assert.Equal("ä\"\\\n", back["a"]); Assert.Equal(1.5, back["b"]); Assert.Equal(true, back["c"]); Assert.Null(back["d"]);
        Assert.Equal(3, ((List<object?>)back["e"]!).Count);
        Assert.Equal("é", MiniJson.Parse("\"\\u00e9\""));
    }

    [Theory] [InlineData("{")] [InlineData("{\"a\":}")] [InlineData("[1,]")] [InlineData("nope")] [InlineData("{} x")]
    public void BadJsonThrows(string s) => Assert.Throws<System.FormatException>(() => MiniJson.Parse(s));

    [Fact]
    public void ProfileRoundtrip()
    {
        var p = new Profile { Name = "Schule 2027", CardWidthMm = 85.6, CardHeightMm = 54, Format = NameFormat.LastCommaFirst, Orientation = "portrait", Stopwords = { "Humboldt" } };
        p.Fields.Add(new FieldRegion { Role = "name", X = 0.35, Y = 0.2, W = 0.6, H = 0.12, Strip = { "Name:" } });
        var q = Profile.FromJson(p.ToJson());
        Assert.Equal("Schule 2027", q.Name); Assert.Equal(NameFormat.LastCommaFirst, q.Format); Assert.Equal("portrait", q.Orientation);
        Assert.Equal(85.6, q.CardWidthMm); Assert.Single(q.Fields); Assert.Equal(0.35, q.Fields[0].X); Assert.Equal("Name:", q.Fields[0].Strip[0]); Assert.Equal("Humboldt", q.Stopwords[0]);
    }

    [Fact]
    public void ProfileStoreLoadsValidAndReportsBroken()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ausweis-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            ProfileStore.Save(dir, new Profile { Name = "Neues Design" });
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "kaputt.json"), "{ nope");
            var problems = new List<string>();
            var list = ProfileStore.Load(dir, problems);
            Assert.Contains(list, p => p.Name == "Automatisch"); Assert.Contains(list, p => p.Name == "Neues Design");
            Assert.Single(problems);
        }
        finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
    }

    [Fact]
    public void SettingsTolerateBrokenFileAndBadValues()
    {
        var f = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(f, "{ kaputt"); Assert.Equal("jpg", Settings.Load(f).Format);
            System.IO.File.WriteAllText(f, "{\"dpi\": 99999, \"jpeg_quality\": 3, \"format\": \"pdf\"}");
            var s = Settings.Load(f); Assert.Equal(300, s.Dpi); Assert.Equal(92, s.JpegQuality); Assert.Equal("pdf", s.Format);
            s.OutputDir = "C:\\Scans"; s.Save(f); Assert.Equal("C:\\Scans", Settings.Load(f).OutputDir);
        }
        finally { System.IO.File.Delete(f); }
    }
}
