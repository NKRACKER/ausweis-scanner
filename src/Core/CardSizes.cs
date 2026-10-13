namespace AusweisScanner.Core;

/// <summary>Bekannte Kartengröße: Standard-Scheckkarte (ID-1) oder vom Nutzer festgelegt. Sie dient dem exakten Einpassen des Rahmens.</summary>
public static class CardSizes
{
    public const double Id1Width = 85.6, Id1Height = 54.0;

    /// <summary>Gültige Größe für die Erkennung: das Design hat Vorrang vor der allgemeinen Einstellung; „automatisch“ = keine Vorgabe.</summary>
    public static bool TryResolve(Settings s, Profile? p, out double w, out double h)
    {
        w = h = 0;
        if (p != null && p.CardWidthMm > 0 && p.CardHeightMm > 0) { w = p.CardWidthMm; h = p.CardHeightMm; return true; }
        switch (s.CardSizeMode)
        {
            case "id1": w = Id1Width; h = Id1Height; return true;
            case "custom": if (s.CardWidthMm > 0 && s.CardHeightMm > 0) { w = s.CardWidthMm; h = s.CardHeightMm; return true; } return false;
            default: return false;
        }
    }

    public static string Describe(double w, double h) => $"{w:0.0} × {h:0.0} mm".Replace('.', ',');
}
