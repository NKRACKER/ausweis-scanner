using System.Collections.Generic;
using System.Linq;

namespace AusweisScanner.Core;

public sealed class OcrWord { public string Text = ""; public RectD Box; }

public sealed class OcrLine
{
    public string Text = "";
    public RectD Box;
    public List<OcrWord> Words = new List<OcrWord>();
    public double Height => Box.H;
}

public sealed class OcrResult
{
    public List<OcrLine> Lines = new List<OcrLine>();
    public string Text => string.Join("\n", Lines.Select(l => l.Text));
}

/// <summary>Texterkennung austauschbar: Windows.Media.Ocr (App), Fake (Tests), später evtl. Tesseract.</summary>
public interface IOcrEngine
{
    string Description { get; }
    OcrResult Recognize(Raster image);
}
