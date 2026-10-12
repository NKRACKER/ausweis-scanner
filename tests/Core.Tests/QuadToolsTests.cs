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
}
