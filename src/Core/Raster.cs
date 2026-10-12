using System;

namespace AusweisScanner.Core;

/// <summary>RGB-Bild (3 Byte/Pixel, Zeile für Zeile). Bewusst ohne System.Drawing, damit der Kern unter Linux testbar bleibt.</summary>
public sealed class Raster
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public Raster(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Leeres Bild");
        Width = width; Height = height; Data = new byte[checked(width * height * 3)];
    }

    public Raster(int width, int height, byte[] data)
    {
        if (data.Length != width * height * 3) throw new ArgumentException("Datenlänge passt nicht");
        Width = width; Height = height; Data = data;
    }

    public void Get(int x, int y, out byte r, out byte g, out byte b)
    {
        int i = (y * Width + x) * 3; r = Data[i]; g = Data[i + 1]; b = Data[i + 2];
    }

    public void Set(int x, int y, byte r, byte g, byte b)
    {
        int i = (y * Width + x) * 3; Data[i] = r; Data[i + 1] = g; Data[i + 2] = b;
    }

    public void Fill(byte r, byte g, byte b)
    {
        for (int i = 0; i < Data.Length; i += 3) { Data[i] = r; Data[i + 1] = g; Data[i + 2] = b; }
    }

    public Raster Clone() => new Raster(Width, Height, (byte[])Data.Clone());

    public Raster Crop(int x, int y, int w, int h)
    {
        x = Math.Max(0, x); y = Math.Max(0, y);
        w = Math.Min(w, Width - x); h = Math.Min(h, Height - y);
        if (w <= 0 || h <= 0) throw new ArgumentException("Ausschnitt außerhalb des Bildes");
        var o = new Raster(w, h);
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(Data, ((y + row) * Width + x) * 3, o.Data, row * w * 3, w * 3);
        return o;
    }

    /// <summary>Dreht im Uhrzeigersinn um n * 90 Grad.</summary>
    public Raster Rotate(int quarterTurns)
    {
        quarterTurns = ((quarterTurns % 4) + 4) % 4;
        if (quarterTurns == 0) return this;
        int nw = quarterTurns == 2 ? Width : Height, nh = quarterTurns == 2 ? Height : Width;
        var o = new Raster(nw, nh);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int nx, ny;
                if (quarterTurns == 1) { nx = Height - 1 - y; ny = x; }
                else if (quarterTurns == 2) { nx = Width - 1 - x; ny = Height - 1 - y; }
                else { nx = y; ny = Width - 1 - x; }
                Get(x, y, out var r, out var g, out var b); o.Set(nx, ny, r, g, b);
            }
        return o;
    }

    /// <summary>Verkleinern per Flächenmittel, Vergrößern bilinear.</summary>
    public Raster Resize(int nw, int nh)
    {
        if (nw == Width && nh == Height) return this;
        var o = new Raster(nw, nh);
        if (nw <= Width && nh <= Height)
        {
            double sx = (double)Width / nw, sy = (double)Height / nh;
            for (int y = 0; y < nh; y++)
            {
                int y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, Math.Min(Height, (int)((y + 1) * sy)));
                for (int x = 0; x < nw; x++)
                {
                    int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, Math.Min(Width, (int)((x + 1) * sx)));
                    long sr = 0, sg = 0, sb = 0; int n = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        int i = (yy * Width + x0) * 3;
                        for (int xx = x0; xx < x1; xx++, i += 3) { sr += Data[i]; sg += Data[i + 1]; sb += Data[i + 2]; n++; }
                    }
                    o.Set(x, y, (byte)(sr / n), (byte)(sg / n), (byte)(sb / n));
                }
            }
        }
        else
        {
            double sx = (double)Width / nw, sy = (double)Height / nh;
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    Sample((x + 0.5) * sx - 0.5, (y + 0.5) * sy - 0.5, out var r, out var g, out var b);
                    o.Set(x, y, r, g, b);
                }
        }
        return o;
    }

    /// <summary>Bilinear; Koordinaten außerhalb werden an den Rand geklemmt.</summary>
    public void Sample(double fx, double fy, out byte r, out byte g, out byte b)
    {
        if (fx < 0) fx = 0; else if (fx > Width - 1) fx = Width - 1;
        if (fy < 0) fy = 0; else if (fy > Height - 1) fy = Height - 1;
        int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        double tx = fx - x0, ty = fy - y0;
        int i00 = (y0 * Width + x0) * 3, i10 = (y0 * Width + x1) * 3, i01 = (y1 * Width + x0) * 3, i11 = (y1 * Width + x1) * 3;
        r = Mix(Data[i00], Data[i10], Data[i01], Data[i11], tx, ty);
        g = Mix(Data[i00 + 1], Data[i10 + 1], Data[i01 + 1], Data[i11 + 1], tx, ty);
        b = Mix(Data[i00 + 2], Data[i10 + 2], Data[i01 + 2], Data[i11 + 2], tx, ty);
    }

    static byte Mix(byte a, byte b, byte c, byte d, double tx, double ty)
        => (byte)(a * (1 - tx) * (1 - ty) + b * tx * (1 - ty) + c * (1 - tx) * ty + d * tx * ty + 0.5);

    public byte[] ToGray()
    {
        var g = new byte[Width * Height];
        for (int i = 0, j = 0; j < g.Length; i += 3, j++) g[j] = (byte)((Data[i] * 77 + Data[i + 1] * 150 + Data[i + 2] * 29) >> 8);
        return g;
    }

    /// <summary>Aus 32-bit-BGRA (wie Windows-Bitmaps/WinRT) mit Zeilenabstand.</summary>
    public static Raster FromBgra(int width, int height, byte[] bgra, int stride)
    {
        var r = new Raster(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0, i = y * stride, o = y * width * 3; x < width; x++, i += 4, o += 3)
            { r.Data[o] = bgra[i + 2]; r.Data[o + 1] = bgra[i + 1]; r.Data[o + 2] = bgra[i]; }
        return r;
    }

    public byte[] ToBgra()
    {
        var o = new byte[Width * Height * 4];
        for (int i = 0, j = 0; i < Data.Length; i += 3, j += 4) { o[j] = Data[i + 2]; o[j + 1] = Data[i + 1]; o[j + 2] = Data[i]; o[j + 3] = 255; }
        return o;
    }
}

public struct PointD
{
    public double X, Y;
    public PointD(double x, double y) { X = x; Y = y; }
    public static PointD operator +(PointD a, PointD b) => new PointD(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new PointD(a.X - b.X, a.Y - b.Y);
    public static PointD operator *(PointD a, double k) => new PointD(a.X * k, a.Y * k);
    public override string ToString() => $"({X:0.#},{Y:0.#})";
}

/// <summary>Rechteck in Pixeln (Texterkennung, Felder).</summary>
public struct RectD
{
    public double X, Y, W, H;
    public RectD(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CenterY => Y + H / 2;
    public double CenterX => X + W / 2;
}
