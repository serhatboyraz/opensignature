namespace OpenSignature.Signing.Formats.Pades;

/// <summary>PDF page rectangle in user space (typically MediaBox/CropBox).</summary>
internal readonly record struct PdfRectangle(double Llx, double Lly, double Urx, double Ury)
{
    public double Width => Urx - Llx;

    public double Height => Ury - Lly;
}
