using OpenSignature.Signing.SmartCard;

namespace OpenSignature.Signing.Pkcs11.Native;

/// <summary>
/// Locates vendor PKCS#11 libraries installed with USB-token / smart-card middleware.
/// </summary>
public static class WellKnownPkcs11Modules
{
    /// <summary>
    /// Sentinel module path used when auto-detect finds nothing so the SmartCard provider
    /// still appears in <c>GET /api/v1/providers</c> as unavailable.
    /// </summary>
    public const string MissingModuleSentinel = "pkcs11-module-not-found";

    private static readonly string[] FileNames =
    [
        "akisp11.dll",
        "eTPKCS11.dll",
        "eToken.dll",
        "aetpkss1.dll",
        "ngp11v211.dll",
        "cmP11.dll",
        "idprimepkcs11.dll",
        "opensc-pkcs11.dll",
        "siecap11.dll",
        "eps2003csp11.dll",
        "libeTPkcs11.dll",
        "bit4xpki.dll",
        "asepkcs.dll",
        "softhsm2-x64.dll",
        "opensc-pkcs11.so",
        "libsofthsm2.so"
    ];

    /// <summary>
    /// When <see cref="SmartCardSigningProviderOptions.AutoDetect"/> is true and
    /// <see cref="Pkcs11ProviderOptionsBase.ModulePath"/> is empty, assign a discovered
    /// module path or <see cref="MissingModuleSentinel"/>.
    /// </summary>
    public static void ApplyAutoDetect(
        SmartCardSigningProviderOptions options,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.AutoDetect || !string.IsNullOrWhiteSpace(options.ModulePath))
        {
            return;
        }

        options.ModulePath = FindExisting(fileExists: fileExists) ?? MissingModuleSentinel;
    }

    /// <summary>
    /// Returns the first existing well-known PKCS#11 library path, or <see langword="null"/>.
    /// </summary>
    public static string? FindExisting(
        IEnumerable<string>? extraDirectories = null,
        IEnumerable<string>? fileNames = null,
        Func<string, bool>? fileExists = null)
    {
        var exists = fileExists ?? File.Exists;
        var names = (fileNames ?? FileNames).Where(static n => !string.IsNullOrWhiteSpace(n)).ToArray();
        var directories = EnumerateSearchDirectories(extraDirectories);

        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                var candidate = Path.GetFullPath(Path.Combine(directory, name.Trim()));
                if (exists(candidate))
                {
                    return candidate;
                }
            }
        }

        foreach (var relative in EnumerateKnownRelativePaths())
        {
            if (exists(relative))
            {
                return relative;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSearchDirectories(IEnumerable<string>? extraDirectories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> yieldUnique(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string full;
                try
                {
                    full = Path.GetFullPath(path.Trim());
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }

                if (seen.Add(full))
                {
                    yield return full;
                }
            }
        }

        if (extraDirectories is not null)
        {
            foreach (var directory in yieldUnique(extraDirectories))
            {
                yield return directory;
            }
        }

        foreach (var directory in yieldUnique(GetPlatformDirectories()))
        {
            yield return directory;
        }
    }

    private static IEnumerable<string> GetPlatformDirectories()
    {
        yield return Environment.SystemDirectory;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            yield return Path.Combine(windows, "System32");
        }

        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86
                 })
        {
            if (Environment.Is64BitProcess && folder == Environment.SpecialFolder.ProgramFilesX86)
            {
                continue;
            }

            var root = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(root))
            {
                yield return root;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return part;
            }
        }

        yield return "/usr/lib";
        yield return "/usr/lib64";
        yield return "/usr/lib/x86_64-linux-gnu";
        yield return "/usr/lib/pkcs11";
        yield return "/usr/local/lib";
    }

    private static IEnumerable<string> EnumerateKnownRelativePaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            yield break;
        }

        string[] relatives =
        [
            @"SafeNet\Authentication\SAC\x64\eTPKCS11.dll",
            @"SafeNet\Authentication\SAC\x64\eToken.dll",
            @"OpenSC Project\OpenSC\pkcs11\opensc-pkcs11.dll",
            @"SoftHSM2\lib\softhsm2-x64.dll",
            @"TUBITAK\KamuSM\akisp11.dll"
        ];

        foreach (var relative in relatives)
        {
            var candidate = Path.Combine(programFiles, relative);
            if (File.Exists(candidate))
            {
                yield return Path.GetFullPath(candidate);
            }
        }
    }
}
