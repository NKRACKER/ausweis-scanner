using System;
using System.Linq;
using AusweisScanner.Core;
using Xunit;

namespace AusweisScanner.Tests;

public class QuadToolsTests
{
    static PointD P(double x, double y) => new PointD(x, y);

    [Fact]
    public void Normalize_OrdersLandscapeRectFromAnyClickOrder()
    {
        var pts = new[] { P(500, 300), P(100, 100), P(100, 300), P(500, 100) };   // wild durcheinander
        var q = QuadTools.Normalize(pts);
        Assert.Equal(100, q[0].X); Assert.Equal(100, q[0].Y);    // links-oben
        Assert.Equal(500, q[1].X); Assert.Equal(100, q[1].Y);    // rechts-oben = lange Kante
        Assert.Equal(500, q[2].X); Assert.Equal(300, q[2].Y);
        Assert.Equal(100, q[3].X); Assert.Equal(300, q[3].Y);
    }

    [Fact]
    public void Normalize_PortraitRectGetsLongEdgeFirst()
    {
        var q = QuadTools.Normalize(new[] { P(100, 100), P(300, 100), P(300, 500), P(100, 500) });
        double w = Math.Abs(q[1].X - q[0].X) + Math.Abs(q[1].Y - q[0].Y), h = Math.Abs(q[3].X - q[0].X) + Math.Abs(q[3].Y - q[0].Y);
        Assert.True(w > h);
    }

    [Fact]
    public void Contains_InsideAndOutside()
    {
        var q = QuadTools.Normalize(new[] { P(0, 0), P(200, 0), P(200, 100), P(0, 100) });
        Assert.True(QuadTools.Contains(q, P(100, 50)));
        Assert.False(QuadTools.Contains(q, P(250, 50)));
        Assert.False(QuadTools.Contains(q, P(100, 150)));
    }

    [Fact]
    public void ToCard_ComputesSizeInMm()
    {
        var c = QuadTools.ToCard(QuadTools.Normalize(new[] { P(0, 0), P(1011, 0), P(1011, 638), P(0, 638) }), 300);
        Assert.InRange(c.WidthMm, 85, 86.5); Assert.InRange(c.HeightMm, 53.5, 54.5);
    }

    [Fact]
    public void DragCorner_KeepsRotationAndOppositeCorner()
    {
        double a = 30 * Math.PI / 180, cs = Math.Cos(a), sn = Math.Sin(a);
        PointD R(double x, double y) => P(100 + x * cs - y * sn, 100 + x * sn + y * cs);
        var q = new[] { R(0, 0), R(200, 0), R(200, 120), R(0, 120) };
        var n = QuadTools.DragCorner(q, 2, R(260, 150));                  // untere rechte Ecke weiter nach außen
        Assert.InRange(Math.Abs(n[0].X - q[0].X), 0, 1e-6); Assert.InRange(Math.Abs(n[0].Y - q[0].Y), 0, 1e-6);   // gegenüberliegende Ecke fix
        Assert.InRange(Math.Abs(n[2].X - R(260, 150).X), 0, 1e-6);
        double w = Math.Sqrt(Math.Pow(n[1].X - n[0].X, 2) + Math.Pow(n[1].Y - n[0].Y, 2)), h = Math.Sqrt(Math.Pow(n[3].X - n[0].X, 2) + Math.Pow(n[3].Y - n[0].Y, 2));
        Assert.InRange(w, 259.9, 260.1); Assert.InRange(h, 149.9, 150.1);
        Assert.InRange(Math.Atan2(n[1].Y - n[0].Y, n[1].X - n[0].X) * 180 / Math.PI, 29.9, 30.1);                  // Drehung bleibt
    }

    [Fact]
    public void RotateTo_KeepsSizeAndCenter()
    {
        var q = QuadTools.Normalize(new[] { P(0, 0), P(200, 0), P(200, 100), P(0, 100) });
        var n = QuadTools.RotateTo(q, 1, P(100 + 100 * 0.5, 50 + 100 * 0.866));   // Ecke 1 auf 60° gedreht
        Assert.InRange(Math.Sqrt(Math.Pow(n[1].X - n[0].X, 2) + Math.Pow(n[1].Y - n[0].Y, 2)), 199.9, 200.1);
        Assert.InRange(n.Average(t => t.X), 99.9, 100.1); Assert.InRange(n.Average(t => t.Y), 49.9, 50.1);
    }
}
