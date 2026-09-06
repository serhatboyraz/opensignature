namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// Loaded PKCS#11 module (shared library / DLL).
/// </summary>
public interface IPkcs11Library : IDisposable
{
    /// <summary>Configured module path or logical module name.</summary>
    string ModulePath { get; }

    /// <summary>True when the module initialized successfully and can enumerate slots.</summary>
    bool IsAvailable { get; }

    /// <summary>Optional detail when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableDetail { get; }

    /// <summary>Enumerates slots; optionally only those with a token present.</summary>
    IReadOnlyList<IPkcs11Slot> GetSlots(bool tokenPresentOnly = true);

    /// <summary>Resolves a slot by id.</summary>
    IPkcs11Slot GetSlot(ulong slotId);
}
