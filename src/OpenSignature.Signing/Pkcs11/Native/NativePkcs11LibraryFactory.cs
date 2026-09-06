using System.Runtime.InteropServices;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace OpenSignature.Signing.Pkcs11.Native;

/// <summary>
/// Production <see cref="IPkcs11LibraryFactory"/> that loads vendor PKCS#11 modules (USB tokens, smart cards, HSMs).
/// Successful loads are cached per process; <c>C_Finalize</c> runs when the factory is disposed.
/// </summary>
public sealed class NativePkcs11LibraryFactory : IPkcs11LibraryFactory, IDisposable
{
    private static int _linuxResolverInstalled;

    private readonly Pkcs11InteropFactories _factories = new();
    private readonly Dictionary<string, NativePkcs11Library> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _disposed;

    /// <inheritdoc />
    public IPkcs11Library Load(string modulePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(modulePath))
        {
            throw new ArgumentException("Module path must not be empty.", nameof(modulePath));
        }

        var path = modulePath.Trim();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_cache.TryGetValue(path, out var cached))
            {
                return cached;
            }

            var loaded = LoadUncached(path);
            if (loaded.IsAvailable)
            {
                _cache[path] = loaded;
            }

            return loaded;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var library in _cache.Values)
            {
                library.ReleaseNative();
            }

            _cache.Clear();
        }
    }

    private NativePkcs11Library LoadUncached(string path)
    {
        if (string.Equals(path, WellKnownPkcs11Modules.MissingModuleSentinel, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            return NativePkcs11Library.CreateUnavailable(
                path,
                $"PKCS#11 module '{path}' was not found. Install the USB token middleware or set Signing:SmartCard:ModulePath to the vendor PKCS#11 library.");
        }

        EnsureLinuxDllImportResolver();

        try
        {
            var interop = _factories.Pkcs11LibraryFactory.LoadPkcs11Library(
                _factories,
                path,
                AppType.MultiThreaded);
            return new NativePkcs11Library(path, interop);
        }
        catch (Exception ex) when (
            ex is Pkcs11Exception
            or DllNotFoundException
            or BadImageFormatException
            or IOException)
        {
            return NativePkcs11Library.CreateUnavailable(
                path,
                $"Failed to load PKCS#11 module '{path}'. The library must match the process architecture (x64 vs x86).");
        }
    }

    private static void EnsureLinuxDllImportResolver()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if (Interlocked.Exchange(ref _linuxResolverInstalled, 1) == 1)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(
            typeof(Pkcs11InteropFactories).Assembly,
            static (libraryName, assembly, searchPath) =>
            {
                if (libraryName == "libdl")
                {
                    return NativeLibrary.Load("libdl.so.2", assembly, searchPath);
                }

                return IntPtr.Zero;
            });
    }
}
