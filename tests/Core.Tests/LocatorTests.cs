using System;
using System.Collections.Generic;
using System.Linq;
using AusweisScanner.Core;
using Xunit;

namespace AusweisScanner.Tests;

/// <summary>
/// Simulierte OCR: Karten sind einfarbige Rasters; Feld-Ausschnitte werden an ihrer Farbe erkannt,
/// Ganzkarten-OCR liefert das scripted Layout. Pixel (2,2) rot = Karte steht aufrecht (sonst Kauderwelsch).
/// </summary>
sealed class FakeOcr : IOcrEngine
{
    public string Description => "fake";
    public Dictionary<(byte, byte, byte), string> FieldTexts = new Dictionary<(byte, byte, byte), string>();
    public List<OcrLine> FullLayout = new List<OcrLine>();
    public int Calls;

    public OcrResult Recognize(Raster image)
    {
        Calls++;
        var res = new OcrResult();
        bool upright = IsUpright(image);
        if (image.Width > 1200)
        {
            if (!upright) { res.Lines.Add(Line("ïı1 ##ß 23 xx", 0, 0, 100, 20)); return res; }
            res.Lines.AddRange(FullLayout); return res;
        }
        image.Get(image.Width / 2, image.Height / 2, out var r, out var g, out var b);
        if (FieldTexts.TryGetValue((r, g, b), out var t)) res.Lines.Add(Line(t, 0, 0, 100, 20));
        return res;
    }

    static bool IsUpright(Raster i) { i.Get(18, 18, out var r, out var g, out var b); return r > 200 && g < 60 && b < 60; } // 16 px Rand durch Prepare()

    public static OcrLine Line(string text, double x, double y, double w, double h)
    {
        var l = new OcrLine { Text = text, Box = new RectD(x, y, w, h) };
        double cx = x; foreach (var tok in text.Split(' ')) { double tw = w * tok.Length / Math.Max(1, text.Length); l.Words.Add(new OcrWord { Text = tok, Box = new RectD(cx, y, tw, h) }); cx += tw + 2; }
        return l;
    }
}

public class NameLocatorTests
{
    static Raster Card(bool upright = true)
    {
        var r = new Raster(856, 540); r.Fill(240, 240, 240);
        if (upright) for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++) r.Set(x, y, 255, 0, 0);
        else for (int y = 534; y < 540; y++) for (int x = 850; x < 856; x++) r.Set(x, y, 255, 0, 0);
        return r;
    }

    [Fact]
    public void ProfileFields_TwoSeparateFields()
    {
        var card = Card();
        void Paint(double x, double y, double w, double h, byte c) { for (int yy = (int)(y * 540); yy < (y + h) * 540; yy++) for (int xx = (int)(x * 856); xx < (x + w) * 856; xx++) card.Set(xx, yy, c, c, c); }
        Paint(0.35, 0.30, 0.55, 0.10, 11); Paint(0.35, 0.50, 0.55, 0.10, 22);
        var ocr = new FakeOcr(); ocr.FieldTexts[(11, 11, 11)] = "MÜLLER"; ocr.FieldTexts[(22, 22, 22)] = "ANNA-MARIA";
        var p = new Profile { Name = "t" };
        p.Fields.Add(new FieldRegion { Role = "last", X = 0.35, Y = 0.30, W = 0.55, H = 0.10 });
        p.Fields.Add(new FieldRegion { Role = "first", X = 0.35, Y = 0.50, W = 0.55, H = 0.10 });
        var c = new NameLocator(ocr).Locate(card, p);
        Assert.Equal("Müller", c[0].Name.Last); Assert.Equal("Anna-Maria", c[0].Name.First); Assert.Equal("Profilfeld", c[0].Source);
        Assert.Equal(2, ocr.Calls);   // gutes Ergebnis → keine teure Ganzkarten-OCR mehr
    }

    [Fact]
    public void Anchors_SingleNameLabelWithComma()
    {
        var ocr = new FakeOcr();
        ocr.FullLayout.AddRange(new[]
        {
            FakeOcr.Line("Schülerausweis", 100, 20, 500, 60), FakeOcr.Line("Gymnasium Musterstadt", 100, 90, 400, 30),
            FakeOcr.Line("Name: Mustermann, Max", 100, 200, 450, 40), FakeOcr.Line("Klasse 7b", 100, 260, 200, 30),
        });
        var c = new NameLocator(ocr).Locate(Card(), Profile.Auto());
        Assert.Equal("Mustermann", c[0].Name.Last); Assert.Equal("Max", c[0].Name.First); Assert.Equal("Beschriftung", c[0].Source);
    }

    [Fact]
    public void Anchors_LabelsAboveValuesOnSeparateLines()
    {
        var ocr = new FakeOcr();
        ocr.FullLayout.AddRange(new[]
        {
            FakeOcr.Line("Nachname", 100, 100, 160, 22), FakeOcr.Line("SCHMIDT", 100, 128, 220, 44),
            FakeOcr.Line("Vorname", 100, 200, 140, 22), FakeOcr.Line("Lena Sophie", 100, 228, 260, 44),
            FakeOcr.Line("Geburtsdatum 01.02.2012", 100, 320, 400, 24),
        });
        var c = new NameLocator(ocr).Locate(Card(), Profile.Auto());
        Assert.Equal("Schmidt", c[0].Name.Last); Assert.Equal("Lena Sophie", c[0].Name.First); Assert.Equal("Beschriftung", c[0].Source);
    }

    [Fact]
    public void Anchors_ToleratesOcrTypoInLabel()
    {
        var ocr = new FakeOcr();
        ocr.FullLayout.AddRange(new[] { FakeOcr.Line("Nachnarne Becker", 100, 100, 300, 30), FakeOcr.Line("Vomame Tom", 100, 150, 300, 30) });
        var c = new NameLocator(ocr).Locate(Card(), Profile.Auto());
        Assert.Equal("Becker", c[0].Name.Last); Assert.Equal("Tom", c[0].Name.First);
    }

    [Fact]
    public void Heuristic_NoLabels_PicksBiggestNameLikeLine()
    {
        var ocr = new FakeOcr();
        ocr.FullLayout.AddRange(new[]
        {
            FakeOcr.Line("SCHÜLERAUSWEIS", 50, 20, 600, 50), FakeOcr.Line("GYMNASIUM AM PARK", 50, 80, 500, 40),
            FakeOcr.Line("Tom Becker", 100, 220, 400, 70), FakeOcr.Line("Gültig bis 31.07.2027", 100, 400, 400, 25),
        });
        var c = new NameLocator(ocr).Locate(Card(), Profile.Auto());
        Assert.Equal("Becker", c[0].Name.Last); Assert.Equal("Tom", c[0].Name.First); Assert.Equal("Schätzung", c[0].Source);
        Assert.True(c[0].Score < 0.7);   // Schätzungen werden der UI als „unsicher“ gemeldet
    }

    [Fact]
    public void UnknownDesign_ProfileFieldsMiss_FallsBackToAnchors()
    {
        // Das Design hat sich geändert: Profilfeld liegt jetzt ins Leere → es darf nicht bei „leer“ enden.
        var ocr = new FakeOcr();
        ocr.FullLayout.Add(FakeOcr.Line("Name: Meier, Hans", 100, 200, 400, 40));
        var p = new Profile { Name = "alt" }; p.Fields.Add(new FieldRegion { Role = "name", X = 0.1, Y = 0.1, W = 0.3, H = 0.1 });
        var c = new NameLocator(ocr).Locate(Card(), p);
        Assert.Equal("Meier", c[0].Name.Last); Assert.Equal("Beschriftung", c[0].Source);
    }

    [Fact]
    public void OrientationResolver_FindsUpsideDownCard()
    {
        var ocr = new FakeOcr(); ocr.FullLayout.Add(FakeOcr.Line("Name: Meier, Hans", 100, 200, 400, 40));
        var loc = new NameLocator(ocr);
        var img = OrientationResolver.Resolve(Card(upright: false), "landscape", c => loc.Locate(c, Profile.Auto()), out var best, out var turns);
        Assert.Equal(2, turns); Assert.Equal("Meier", best[0].Name.Last);
        var upright = OrientationResolver.Resolve(Card(), "landscape", c => loc.Locate(c, Profile.Auto()), out _, out var t2);
        Assert.Equal(0, t2); Assert.Equal(856, upright.Width);
    }

    [Fact]
    public void OrientationResolver_PortraitDesign_TriesQuarterTurns()
    {
        var calls = new List<int>(); int i = 0;
        var card = new Raster(100, 60);
        OrientationResolver.Resolve(card, "portrait", c => { calls.Add(c.Width); i++; return i == 2 ? new List<NameCandidate> { new NameCandidate { Score = 0.9 } } : new List<NameCandidate>(); }, out _, out var turns);
        Assert.Equal(3, turns); Assert.Equal(new[] { 60, 60 }, calls.ToArray());
    }

    [Fact]
    public void NothingFound_ReturnsEmptyNotException()
    {
        var c = new NameLocator(new FakeOcr()).Locate(Card(), Profile.Auto());
        Assert.Empty(c);
    }
}

public class ProfileMatcherTests
{
    static Raster CardWithFieldAt(double x, double y, byte color)
    {
        var r = new Raster(856, 540); r.Fill(240, 240, 240);
        for (int yy = (int)(y * 540); yy < (y + 0.1) * 540; yy++) for (int xx = (int)(x * 856); xx < (x + 0.5) * 856; xx++) r.Set(xx, yy, color, color, color);
        return r;
    }

    [Fact]
    public void PicksTheMatchingDesignAndRemembersIt()
    {
        var ocr = new FakeOcr(); ocr.FieldTexts[(50, 50, 50)] = "Meier, Hans"; ocr.FieldTexts[(60, 60, 60)] = "Schulz, Eva";
        var designOld = new Profile { Name = "alt" }; designOld.Fields.Add(new FieldRegion { Role = "name", X = 0.1, Y = 0.7, W = 0.5, H = 0.1 });
        var designNew = new Profile { Name = "neu" }; designNew.Fields.Add(new FieldRegion { Role = "name", X = 0.4, Y = 0.2, W = 0.5, H = 0.1 });
        var m = new ProfileMatcher(new NameLocator(ocr), new[] { Profile.Auto(), designOld, designNew });

        var c1 = m.Locate(CardWithFieldAt(0.4, 0.2, 50), out var used1);
        Assert.Equal("neu", used1.Name); Assert.Equal("Meier", c1[0].Name.Last);
        int callsAfterFirst = ocr.Calls;
        var c2 = m.Locate(CardWithFieldAt(0.4, 0.2, 60), out var used2);
        Assert.Equal("neu", used2.Name); Assert.Equal("Schulz", c2[0].Name.Last);
        Assert.Equal(callsAfterFirst + 1, ocr.Calls);   // zweite Karte: gemerktes Profil zuerst → nur eine Feld-OCR
        Assert.Equal("neu", m.LastUsed!.Name);
    }

    [Fact]
    public void NoProfileMatches_FallsBackToAutoDetection()
    {
        var ocr = new FakeOcr(); ocr.FullLayout.Add(FakeOcr.Line("Name: Neu, Design", 100, 200, 400, 40));
        var old = new Profile { Name = "alt" }; old.Fields.Add(new FieldRegion { Role = "name", X = 0.1, Y = 0.1, W = 0.3, H = 0.1 });
        var m = new ProfileMatcher(new NameLocator(ocr), new[] { Profile.Auto(), old });
        var r = new Raster(856, 540); r.Fill(240, 240, 240); for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++) r.Set(x, y, 255, 0, 0);
        var c = m.Locate(r, out var used);
        Assert.Equal("Automatisch", used.Name); Assert.Equal("Neu", c[0].Name.Last); Assert.Null(m.LastUsed);
    }
}

public class OrientationFallbackTests
{
    [Fact]
    public void LandscapeDesign_StillFindsCardPlacedSideways()
    {
        var tried = new List<int>(); int i = 0;
        OrientationResolver.Resolve(new Raster(100, 60), "landscape", c => { tried.Add(c.Width); i++; return i == 3 ? new List<NameCandidate> { new NameCandidate { Score = 0.9 } } : new List<NameCandidate>(); }, out _, out var turns);
        Assert.Equal(1, turns); Assert.Equal(new[] { 100, 100, 60 }, tried.ToArray());   // 0°, 180°, dann 90°
    }
}

public class EnsembleAndEnhanceTests
{
    static NameCandidate C(string last, string first, double score, int pass) => new NameCandidate { Name = new ParsedName(last, first), Score = score, Pass = pass, Source = "Beschriftung" };

    [Fact]
    public void Merge_ConfirmationFromSecondPassRaisesScore()
    {
        var m = NameLocator.Merge(new[] { C("Eickhoff", "Mieke", 0.81, 0), C("Eickhoff", "Mieke", 0.80, 1) });
        Assert.Single(m); Assert.Equal("Eickhoff", m[0].Name.Last); Assert.InRange(m[0].Score, 0.87, 0.90);
    }

    [Fact]
    public void Merge_OneCharacterMisreadJoinsClusterAndBestSpellingWins()
    {
        var m = NameLocator.Merge(new[] { C("Eickrnann", "Mieke", 0.62, 0), C("Eickmann", "Mieke", 0.81, 1), C("Eickmann", "Mieke", 0.80, 2) });
        Assert.Single(m); Assert.Equal("Eickmann", m[0].Name.Last); Assert.True(m[0].Score > 0.9);
    }

    [Fact]
    public void Merge_DifferentNamesStaySeparate_FirstPassWinsTies()
    {
        var m = NameLocator.Merge(new[] { C("Neumann", "Sophie", 0.81, 0), C("Naumann", "Sophia", 0.81, 1) });
        Assert.Equal(2, m.Count); Assert.Equal("Neumann", m[0].Name.Last);
    }

    [Fact]
    public void Normalize_FlattensShadingAndKeepsText()
    {
        // heller Hintergrund mit starkem Helligkeitsverlauf (Schatten links) und dunklem „Text“-Block
        var r = new Raster(400, 250);
        for (int y = 0; y < 250; y++) for (int x = 0; x < 400; x++) { byte v = (byte)(110 + x * 0.35); r.Set(x, y, v, v, v); }
        for (int y = 100; y < 130; y++) for (int x = 150; x < 260; x++) r.Set(x, y, 20, 20, 20);
        var n = ImageEnhance.Normalize(r);
        byte Left() { n.Get(30, 30, out var a, out _, out _); return a; } byte Right() { n.Get(370, 30, out var a, out _, out _); return a; } byte Ink() { n.Get(200, 115, out var a, out _, out _); return a; }
        Assert.InRange(Math.Abs(Left() - Right()), 0, 25);   // Verlauf ist weg
        Assert.True(Left() > 180 && Right() > 180);          // Hintergrund hell
        Assert.True(Ink() < 70);                              // Text bleibt dunkel
    }
}
