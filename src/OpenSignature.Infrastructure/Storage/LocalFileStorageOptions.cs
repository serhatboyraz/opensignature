namespace OpenSignature.Infrastructure.Storage;

/// <summary>
/// Configuration for <see cref="LocalFileStorage"/>.
/// </summary>
public sealed class LocalFileStorageOptions
{
    public const string SectionName = "Storage:Local";

    /// <summary>
    /// Absolute or relative root directory under which all objects are stored.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}
