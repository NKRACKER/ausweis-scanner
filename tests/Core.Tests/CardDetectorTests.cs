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

    [Theory]
    [InlineData(20)]    // dunkler Deckel
    [InlineData(242)]   // weißer Deckel, weiße Karte
    public void FitCard_FindsCornersFromRoughRegion(int lid)
    {
        var card = new Card { CxMm = 100, CyMm = 120, AngleDeg = 23 };
        var scan = Make(new[] { card, new Card { CxMm = 55, CyMm = 45, AngleDeg = -6 } }, lid: (byte)lid, dpi: 150);
        double px = 150 / 25.4;
        // grober, lockerer Bereich um die Karte (ca. 25 % zu groß, nicht gedreht)
        var roi = new RectD((100 - 60) * px, (120 - 45) * px, 120 * px, 90 * px);
        var fit = CardDetector.FitCard(scan, roi, new DetectorOptions { Dpi = 150 });
        Assert.NotNull(fit);
        Assert.InRange(fit!.WidthMm, 83, 90); Assert.InRange(fit.HeightMm, 52, 58);
        Assert.InRange(Math.Abs(fit.AngleDeg - 23), 0, 2.5);
        var cx = fit.Corners.Average(p => p.X) / px; var cy = fit.Corners.Average(p => p.Y) / px;
        Assert.InRange(cx, 98, 102); Assert.InRange(cy, 118, 122);
    }

    [Fact]
    public void FitCard_EmptyRegion_ReturnsNull()
    {
        var scan = Make(new[] { new Card { CxMm = 55, CyMm = 45, AngleDeg = 0 } }, lid: 20, dpi: 150);
        double px = 150 / 25.4;
        Assert.Null(CardDetector.FitCard(scan, new RectD(120 * px, 200 * px, 40 * px, 30 * px), new DetectorOptions { Dpi = 150 }));
    }

    [Fact]
    public void ScannerShadowBelowCard_IsTrimmedOff()
    {
        // heller Deckel, Karte mit weichem Schatten (ca. 2,5 mm) an der Unterkante – er darf nicht im Zuschnitt landen
        var scan = Make(new[] { new Card { CxMm = 100, CyMm = 120, AngleDeg = 0 } }, lid: 238, dpi: 200);
        double px = 200 / 25.4;
        for (double yy = 147; yy < 149.6; yy += 1 / px)
            for (double xx = 57.2; xx < 142.8; xx += 1 / px)
            {
                double t = (yy - 147) / 2.6; byte v = (byte)(200 + 38 * t);   // dunkel direkt an der Kante, nach außen heller
                scan.Set((int)(xx * px), (int)(yy * px), v, v, v);
            }
        var res = CardDetector.Detect(scan, new DetectorOptions { Dpi = 200, EdgeInsetMm = 0.6 });
        Assert.Single(res.Cards);
        double hMm = res.Cards[0].HeightPx / px;
        Assert.InRange(hMm, 51.5, 54.0);   // 54 mm Karte − 2×0,6 mm Randabzug = 52,8; mit Schatten wären es ≈ 55
    }
}
