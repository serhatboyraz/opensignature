using System.IO.Compression;
using System.Text;

namespace OpenSignature.Signing.Formats.Asic;

/// <summary>
/// ZIP writer/reader for ASiC containers.
/// The <c>mimetype</c> entry is written first, STORED (uncompressed), with an empty extra field
/// so the MIME type string starts at offset 38 (ETSI EN 319 162-1 / OCF convention).
/// </summary>
public static class AsicZip
{
    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const ushort VersionNeeded = 20;
    private const ushort StoreMethod = 0;

    /// <summary>
    /// Builds an ASiC ZIP. <paramref name="mimeType"/> is written as the first STORED entry named <c>mimetype</c>.
    /// </summary>
    public static byte[] Create(string mimeType, IReadOnlyList<AsicZipEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentNullException.ThrowIfNull(entries);

        var mimeBytes = Encoding.ASCII.GetBytes(mimeType);
        var allEntries = new List<AsicZipEntry>(entries.Count + 1)
        {
            new(AsicConstants.MimeTypeEntryName, mimeBytes, Store: true)
        };
        allEntries.AddRange(entries);

        using var output = new MemoryStream();
        var centralRecords = new List<CentralRecord>(allEntries.Count);
        var dosTime = ToDosDateTime(DateTime.UtcNow);

        foreach (var entry in allEntries)
        {
            ValidateEntryName(entry.Name);
            var nameBytes = Encoding.UTF8.GetBytes(entry.Name);
            var data = entry.Data;
            var crc = Crc32Ieee.Compute(data);
            var localOffset = (uint)output.Position;

            WriteUInt32(output, LocalHeaderSignature);
            WriteUInt16(output, VersionNeeded);
            WriteUInt16(output, 0);
            WriteUInt16(output, StoreMethod);
            WriteUInt16(output, dosTime.Time);
            WriteUInt16(output, dosTime.Date);
            WriteUInt32(output, crc);
            WriteUInt32(output, (uint)data.Length);
            WriteUInt32(output, (uint)data.Length);
            WriteUInt16(output, (ushort)nameBytes.Length);
            WriteUInt16(output, 0);
            output.Write(nameBytes);
            output.Write(data);

            centralRecords.Add(new CentralRecord(
                nameBytes,
                crc,
                (uint)data.Length,
                localOffset,
                dosTime.Time,
                dosTime.Date));
        }

        var centralStart = (uint)output.Position;
        foreach (var record in centralRecords)
        {
            WriteUInt32(output, CentralHeaderSignature);
            WriteUInt16(output, VersionNeeded); // version made by
            WriteUInt16(output, VersionNeeded); // version needed
            WriteUInt16(output, 0);
            WriteUInt16(output, StoreMethod);
            WriteUInt16(output, record.DosTime);
            WriteUInt16(output, record.DosDate);
            WriteUInt32(output, record.Crc);
            WriteUInt32(output, record.Size);
            WriteUInt32(output, record.Size);
            WriteUInt16(output, (ushort)record.NameBytes.Length);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt16(output, 0);
            WriteUInt32(output, 0U);
            WriteUInt32(output, record.LocalHeaderOffset);
            output.Write(record.NameBytes);
        }

        var centralSize = (uint)(output.Position - centralStart);
        WriteUInt32(output, EndOfCentralDirectorySignature);
        WriteUInt16(output, 0);
        WriteUInt16(output, 0);
        WriteUInt16(output, (ushort)centralRecords.Count);
        WriteUInt16(output, (ushort)centralRecords.Count);
        WriteUInt32(output, centralSize);
        WriteUInt32(output, centralStart);
        WriteUInt16(output, 0);

        return output.ToArray();
    }

    /// <summary>Reads ASiC ZIP entries (including <c>mimetype</c>).</summary>
    public static IReadOnlyList<AsicZipEntry> Read(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        using var input = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var result = new List<AsicZipEntry>(archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                continue;
            }

            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            result.Add(new AsicZipEntry(entry.FullName.Replace('\\', '/'), buffer.ToArray(), Store: true));
        }

        return result;
    }

    /// <summary>
    /// Returns whether the ZIP starts with an uncompressed <c>mimetype</c> local header (ASiC/OCF layout).
    /// </summary>
    public static bool HasLeadingMimeTypeEntry(byte[] zipBytes, string expectedMimeType)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMimeType);

        const int headerSize = 30;
        var name = Encoding.UTF8.GetBytes(AsicConstants.MimeTypeEntryName);
        var mime = Encoding.ASCII.GetBytes(expectedMimeType);
        if (zipBytes.Length < headerSize + name.Length + mime.Length)
        {
            return false;
        }

        if (ReadUInt32(zipBytes, 0) != LocalHeaderSignature)
        {
            return false;
        }

        var compression = ReadUInt16(zipBytes, 8);
        var fileNameLength = ReadUInt16(zipBytes, 26);
        var extraLength = ReadUInt16(zipBytes, 28);
        if (compression != 0 || extraLength != 0 || fileNameLength != name.Length)
        {
            return false;
        }

        if (!zipBytes.AsSpan(headerSize, name.Length).SequenceEqual(name))
        {
            return false;
        }

        return zipBytes.AsSpan(headerSize + name.Length, mime.Length).SequenceEqual(mime);
    }

    /// <summary>Returns a safe data-object name for ASiC (no path traversal, not reserved).</summary>
    public static string SanitizeDataEntryName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return AsicConstants.DefaultDataEntryName;
        }

        var normalized = fileName.Replace('\\', '/').Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Equals(AsicConstants.MimeTypeEntryName, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase))
        {
            return AsicConstants.DefaultDataEntryName;
        }

        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Contains('\\', StringComparison.Ordinal)
            || name.Contains("..", StringComparison.Ordinal)
            || name.StartsWith('/'))
        {
            throw new InvalidOperationException($"Invalid ASiC entry name '{name}'.");
        }
    }

    private static (ushort Time, ushort Date) ToDosDateTime(DateTime utc)
    {
        var year = Math.Clamp(utc.Year, 1980, 2107);
        var date = (ushort)(((year - 1980) << 9) | (utc.Month << 5) | utc.Day);
        var time = (ushort)((utc.Hour << 11) | (utc.Minute << 5) | (utc.Second / 2));
        return (time, date);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private sealed record CentralRecord(
        byte[] NameBytes,
        uint Crc,
        uint Size,
        uint LocalHeaderOffset,
        ushort DosTime,
        ushort DosDate);
}

/// <summary>A file inside an ASiC ZIP container.</summary>
public sealed record AsicZipEntry(string Name, byte[] Data, bool Store = true);
