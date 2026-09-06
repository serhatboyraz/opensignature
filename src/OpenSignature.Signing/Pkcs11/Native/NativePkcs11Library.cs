using Net.Pkcs11Interop.Common;
using InteropPkcs11Library = Net.Pkcs11Interop.HighLevelAPI.IPkcs11Library;
using InteropSlot = Net.Pkcs11Interop.HighLevelAPI.ISlot;

namespace OpenSignature.Signing.Pkcs11.Native;

/// <summary>
/// Native PKCS#11 module loaded through Pkcs11Interop. Private keys are never exported.
/// </summary>
internal sealed class NativePkcs11Library : IPkcs11Library
{
    private InteropPkcs11Library? _library;
    private bool _disposed;

    public NativePkcs11Library(string modulePath, InteropPkcs11Library library)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentNullException.ThrowIfNull(library);

        ModulePath = modulePath.Trim();
        _library = library;
        IsAvailable = true;
    }

    private NativePkcs11Library(string modulePath, string unavailableDetail)
    {
        ModulePath = modulePath.Trim();
        IsAvailable = false;
        UnavailableDetail = unavailableDetail;
    }

    public string ModulePath { get; }

    public bool IsAvailable { get; }

    public string? UnavailableDetail { get; }

    public static NativePkcs11Library CreateUnavailable(string modulePath, string detail) =>
        new(modulePath, detail);

    public IReadOnlyList<IPkcs11Slot> GetSlots(bool tokenPresentOnly = true)
    {
        ThrowIfDisposed();
        EnsureAvailable();

        var slotType = tokenPresentOnly
            ? SlotsType.WithTokenPresent
            : SlotsType.WithOrWithoutTokenPresent;

        return _library!.GetSlotList(slotType)
            .Select(static slot => (IPkcs11Slot)new NativePkcs11Slot(slot))
            .ToArray();
    }

    public IPkcs11Slot GetSlot(ulong slotId)
    {
        ThrowIfDisposed();
        EnsureAvailable();

        var slots = GetSlots(tokenPresentOnly: false);
        var match = slots.FirstOrDefault(s => s.SlotId == slotId);
        return match ?? throw new InvalidOperationException($"PKCS#11 slot '{slotId}' was not found.");
    }

    public void Dispose()
    {
        _disposed = true;
    }

    internal void ReleaseNative()
    {
        _disposed = true;
        _library?.Dispose();
        _library = null;
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable || _library is null)
        {
            throw new InvalidOperationException(
                UnavailableDetail ?? $"PKCS#11 module '{ModulePath}' is unavailable.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal sealed class NativePkcs11Slot : IPkcs11Slot
{
    private readonly InteropSlot _slot;

    public NativePkcs11Slot(InteropSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        _slot = slot;
        SlotId = slot.SlotId;

        try
        {
            var info = slot.GetSlotInfo();
            Description = TrimOrNull(info.SlotDescription);
            TokenPresent = info.SlotFlags.TokenPresent;
        }
        catch (Pkcs11Exception)
        {
            TokenPresent = false;
        }

        if (!TokenPresent)
        {
            return;
        }

        try
        {
            TokenLabel = TrimOrNull(slot.GetTokenInfo().Label);
        }
        catch (Pkcs11Exception)
        {
            TokenPresent = false;
        }
    }

    public ulong SlotId { get; }

    public string? Description { get; }

    public string? TokenLabel { get; }

    public bool TokenPresent { get; private set; }

    public IPkcs11Session OpenSession(bool readWrite = false)
    {
        if (!TokenPresent)
        {
            throw new InvalidOperationException($"PKCS#11 slot '{SlotId}' has no token present.");
        }

        var session = _slot.OpenSession(readWrite ? SessionType.ReadWrite : SessionType.ReadOnly);
        return new NativePkcs11Session(session);
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
