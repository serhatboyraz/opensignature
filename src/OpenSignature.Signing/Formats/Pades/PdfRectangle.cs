namespace OpenSignature.Signing.Formats.Pades;

/// <summary>PDF page rectangle in user space (typically MediaBox/CropBox).</summary>
internal readonly record struct PdfRectangle(double Llx, double Lly, double Urx, double Ury)
{
    public double Width => Urx - Llx;

    public double Height => Ury - Lly;

    public bool HasArea => Width > 0 && Height > 0;

    public bool Overlaps(PdfRectangle other) =>
        Llx < other.Urx && Urx > other.Llx && Lly < other.Ury && Ury > other.Lly;

    public PdfRectangle Inflate(double amount) =>
        new(Llx - amount, Lly - amount, Urx + amount, Ury + amount);
}
