using System;
using System.Linq;
using AusweisScanner.Core;
using Xunit;
using static AusweisScanner.Tests.SyntheticScan;

namespace AusweisScanner.Tests;

public class CardDetectorTests
{
    static Card[] Six() => new[]
    {
        new Card { CxMm = 55, CyMm = 45, AngleDeg = -6 },  new Card { CxMm = 155, CyMm = 45, AngleDeg = 4 },
        new Card { CxMm = 55, CyMm = 130, AngleDeg = 0 },  new Card { CxMm = 155, CyMm = 130, AngleDeg = 12 },
        new Card { CxMm = 55, CyMm = 215, AngleDeg = 90 }, new Card { CxMm = 155, CyMm = 215, AngleDeg = 180 },
    };

    [Theory]
    [InlineData(20, 150)]    // dunkler Deckel
    [InlineData(120, 150)]   // grauer Deckel
    [InlineData(242, 150)]   // weißer Deckel, weiße Karte (nur Kante/Schatten)
    [InlineData(20, 300)]    // volle Auflösung
    public void FindsAllCardsRegardlessOfLid(int lid, int dpi)
    {
        var scan = Make(Six(), (byte)lid, dpi);
        var res = Detect(scan, dpi);
        Assert.Equal(6, res.Cards.Count);
        foreach (var c in res.Cards)
        {
            Assert.InRange(c.WidthMm, 83.5, 89.5); Assert.InRange(c.HeightMm, 52.0, 57.5);  // Schattenrand zählt mit
            Assert.True(c.WidthPx > c.HeightPx);
        }
    }

    static DetectionResult Detect(Raster scan, int dpi, Action<DetectorOptions>? tweak = null)
    {
        var o = new DetectorOptions { Dpi = dpi }; tweak?.Invoke(o);
        return CardDetector.Detect(scan, o);
    }

    [Fact]
    public void ReadingOrderIsRowsLeftToRight()
    {
        var res = Detect(Make(Six(), 20, 150), 150);
        var cx = res.Cards.Select(c => c.Corners.Average(p => p.X)).ToArray();
        var cy = res.Cards.Select(c => c.Corners.Average(p => p.Y)).ToArray();
        Assert.True(cx[0] < cx[1] && cx[2] < cx[3] && cx[4] < cx[5]);
        Assert.True(cy[0] < cy[2] && cy[2] < cy[4]);
    }

    [Fact]
    public void ExtractionIsDeskewedLandscape()
    {
        var scan = Make(new[] { new Card { CxMm = 105, CyMm = 100, AngleDeg = 9, Fill = 240 } }, 20, 150);
        var res = Detect(scan, 150);
        var card = Assert.Single(res.Cards);
        var img = CardExtractor.Extract(scan, card);
        Assert.True(img.Width > img.Height);
        Assert.InRange((double)img.Width / img.Height, 1.5, 1.66);
        // Foto (dunkel) liegt links, Randzone ringsum ist hell (kein Hintergrund im Ausschnitt)
        img.Get((int)(img.Width * 0.18), (int)(img.Height * 0.55), out var photo, out _, out _);
        Assert.True(photo < 120);
        for (int x = img.Width / 10; x < img.Width * 9 / 10; x += 7)   // Ecken sind abgerundet
        { img.Get(x, img.Height - 3, out var r, out _, out _); Assert.True(r > 100, $"Unterer Rand dunkel bei x={x}: {r}"); }
    }

    [Fact]
    public void IgnoresDustAndSmallObjects()
    {
        var cards = new[] { new Card { CxMm = 60, CyMm = 60, AngleDeg = 2 }, new Card { CxMm = 150, CyMm = 200, WMm = 25, HMm = 15 } };
        var res = Detect(Make(cards, 20, 150, dust: true), 150);
        Assert.Single(res.Cards);
    }

    [Fact]
    public void TouchingCardsAreNotAcceptedAsOne()
    {
        var cards = new[] { new Card { CxMm = 60, CyMm = 100 }, new Card { CxMm = 60 + 85.6 + 0.2, CyMm = 100 } };
        var res = Detect(Make(cards, 20, 150), 150);
        Assert.DoesNotContain(res.Cards, c => c.WidthMm > 120);
        Assert.NotEmpty(res.Warnings);
    }

    [Fact]
    public void ExpectedSizeFilterRejectsOtherFormats()
    {
        var cards = new[] { new Card { CxMm = 60, CyMm = 60 }, new Card { CxMm = 150, CyMm = 150, WMm = 105, HMm = 74 } };
        var res = Detect(Make(cards, 20, 150), 150, o => { o.ExpectedWidthMm = 85.6; o.ExpectedHeightMm = 54; });
        Assert.Single(res.Cards);
    }

    [Fact]
    public void EmptyBedGivesNoCards()
    {
        var res = Detect(Make(Array.Empty<Card>(), 30, 150), 150);
        Assert.Empty(res.Cards);
    }

    [Fact]
    public void NoisyLidStillWorks()
    {
        var res = Detect(Make(Six(), 200, 150, noise: 12), 150);
        Assert.Equal(6, res.Cards.Count);
    }
}
