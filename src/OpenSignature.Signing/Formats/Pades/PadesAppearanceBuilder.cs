using System.Globalization;
using System.Text;

namespace OpenSignature.Signing.Formats.Pades;

/// <summary>Builds a visible signature Form XObject and widget rectangle.</summary>
internal static class PadesAppearanceBuilder
{
    public const double DefaultWidth = 180;
    public const double DefaultHeight = 54;
    public const double Margin = 36;
    public const double Gap = 8;

    public static PadesAppearanceResources Build(
        PdfRectangle pageBox,
        string signerName,
        DateTimeOffset signingTimeUtc,
        string? note,
        PdfImageXObject? image,
        IReadOnlyList<PdfRectangle>? occupiedRects = null)
    {
        var width = DefaultWidth;
        var height = image is null ? DefaultHeight : 72;
        var rect = PlaceWithoutOverlap(pageBox, width, height, occupiedRects);

        var lines = new List<string>
        {
            "Digitally signed by " + signerName,
            "Date: " + signingTimeUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
        };
        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add(TrimLine(note, 80));
        }

        var content = new StringBuilder();
        content.Append("q\n");
        content.Append("0.95 0.97 0.96 rg\n");
        content.Append(CultureInfo.InvariantCulture, $"0 0 {PdfLiteral.Number(width)} {PdfLiteral.Number(height)} re f\n");
        content.Append("0.05 0.43 0.42 RG\n1 w\n");
        content.Append(CultureInfo.InvariantCulture, $"0.5 0.5 {PdfLiteral.Number(width - 1)} {PdfLiteral.Number(height - 1)} re S\n");

        var textLeft = 6.0;
        if (image is not null)
        {
            var imageBox = 40.0;
            var scale = Math.Min(imageBox / image.Width, imageBox / image.Height);
            var drawW = image.Width * scale;
            var drawH = image.Height * scale;
            var imageX = 6;
            var imageY = (height - drawH) / 2;
            content.Append("q\n");
            content.Append(CultureInfo.InvariantCulture,
                $"{PdfLiteral.Number(drawW)} 0 0 {PdfLiteral.Number(drawH)} {PdfLiteral.Number(imageX)} {PdfLiteral.Number(imageY)} cm\n");
            content.Append("/Im0 Do\nQ\n");
            textLeft = imageX + drawW + 6;
        }

        content.Append("BT\n/Helv 7 Tf\n0 0 0 rg\n");
        var y = height - 16;
        for (var i = 0; i < lines.Count; i++)
        {
            content.Append(CultureInfo.InvariantCulture, $"1 0 0 1 {PdfLiteral.Number(textLeft)} {PdfLiteral.Number(y)} Tm\n");
            content.Append(PdfLiteral.String(TrimLine(lines[i], 42)));
            content.Append(" Tj\n");
            y -= 11;
        }

        content.Append("ET\nQ\n");

        return new PadesAppearanceResources(
            Rect: rect,
            BBoxWidth: width,
            BBoxHeight: height,
            ContentStream: Encoding.ASCII.GetBytes(content.ToString()),
            Image: image);
    }

    private static PdfRectangle PlaceWithoutOverlap(
        PdfRectangle page,
        double width,
        double height,
        IReadOnlyList<PdfRectangle>? occupiedRects)
    {
        var maxWidth = Math.Max(24, page.Width - (Margin * 2));
        var maxHeight = Math.Max(16, page.Height - (Margin * 2));
        width = Math.Min(width, maxWidth);
        height = Math.Min(height, maxHeight);

        var occupied = occupiedRects ?? [];
        var stepX = width + Gap;
        var stepY = height + Gap;
        var columns = Math.Max(1, (int)Math.Floor((page.Width - (Margin * 2) + Gap) / stepX));
        var rows = Math.Max(1, (int)Math.Floor((page.Height - (Margin * 2) + Gap) / stepY));

        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                var urx = page.Urx - Margin - (column * stepX);
                var llx = urx - width;
                var lly = page.Lly + Margin + (row * stepY);
                var ury = lly + height;
                if (llx < page.Llx + Margin || ury > page.Ury - Margin)
                {
                    continue;
                }

                var candidate = new PdfRectangle(llx, lly, urx, ury);
                if (!OverlapsAny(candidate, occupied))
                {
                    return candidate;
                }
            }
        }

        return PlaceBottomRight(page, width, height);
    }

    private static bool OverlapsAny(PdfRectangle candidate, IReadOnlyList<PdfRectangle> occupied)
    {
        var inflated = candidate.Inflate(Gap);
        foreach (var existing in occupied)
        {
            if (!existing.HasArea)
            {
                continue;
            }

            if (inflated.Overlaps(existing))
            {
                return true;
            }
        }

        return false;
    }

    private static PdfRectangle PlaceBottomRight(PdfRectangle page, double width, double height)
    {
        var maxWidth = Math.Max(24, page.Width - (Margin * 2));
        var maxHeight = Math.Max(16, page.Height - (Margin * 2));
        width = Math.Min(width, maxWidth);
        height = Math.Min(height, maxHeight);

        var llx = page.Urx - Margin - width;
        var lly = page.Lly + Margin;
        if (llx < page.Llx + Margin)
        {
            llx = page.Llx + Margin;
        }

        if (lly + height > page.Ury - Margin)
        {
            lly = page.Ury - Margin - height;
        }

        return new PdfRectangle(llx, lly, llx + width, lly + height);
    }

    private static string TrimLine(string value, int maxChars)
    {
        var trimmed = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..(maxChars - 3)] + "...";
    }
}

internal sealed record PadesAppearanceResources(
    PdfRectangle Rect,
    double BBoxWidth,
    double BBoxHeight,
    byte[] ContentStream,
    PdfImageXObject? Image);
