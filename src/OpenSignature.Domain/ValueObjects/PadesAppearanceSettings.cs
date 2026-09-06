namespace OpenSignature.Domain.ValueObjects;

/// <summary>
/// Optional visible PAdES stamp (page widget appearance). Invisible when <see cref="Visible"/> is false.
/// </summary>
public sealed class PadesAppearanceSettings
{
    public const int MaxNoteLength = 500;

    public const int DefaultPageNumber = 1;

    private PadesAppearanceSettings()
    {
        PageNumber = DefaultPageNumber;
    }

    private PadesAppearanceSettings(bool visible, string? note, Guid? imageFileId, int pageNumber)
    {
        Visible = visible;
        Note = note;
        ImageFileId = imageFileId;
        PageNumber = pageNumber;
    }

    /// <summary>When true, the PDF signature widget is drawn on the page.</summary>
    public bool Visible { get; private set; }

    /// <summary>Optional note shown on the stamp (and stored as PDF /Reason).</summary>
    public string? Note { get; private set; }

    /// <summary>Optional appearance image stored as a <c>StoredFile</c> (JPEG or PNG).</summary>
    public Guid? ImageFileId { get; private set; }

    /// <summary>1-based page index for the visible widget. Default is the first page.</summary>
    public int PageNumber { get; private set; }

    public static PadesAppearanceSettings Invisible { get; } = new(
        visible: false,
        note: null,
        imageFileId: null,
        pageNumber: DefaultPageNumber);

    public static PadesAppearanceSettings Create(
        bool visible,
        string? note = null,
        Guid? imageFileId = null,
        int pageNumber = DefaultPageNumber)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Appearance page number must be at least 1.");
        }

        if (imageFileId is Guid id && id == Guid.Empty)
        {
            throw new ArgumentException("Appearance image file ID must not be empty when provided.", nameof(imageFileId));
        }

        string? normalizedNote = null;
        if (note is not null)
        {
            var trimmed = note.Trim();
            if (trimmed.Length > MaxNoteLength)
            {
                throw new ArgumentException(
                    $"Signature note must not exceed {MaxNoteLength} characters.",
                    nameof(note));
            }

            normalizedNote = trimmed.Length == 0 ? null : trimmed;
        }

        if (!visible && normalizedNote is null && imageFileId is null)
        {
            return new PadesAppearanceSettings(
                visible: false,
                note: null,
                imageFileId: null,
                pageNumber: pageNumber == DefaultPageNumber ? DefaultPageNumber : pageNumber);
        }

        return new PadesAppearanceSettings(
            visible,
            normalizedNote,
            imageFileId,
            pageNumber);
    }
}
