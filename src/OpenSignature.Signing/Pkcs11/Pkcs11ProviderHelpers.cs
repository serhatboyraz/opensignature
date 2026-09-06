using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// Shared helpers for PKCS#11-backed signing providers (slot selection, PIN resolution).
/// Never logs PIN values.
/// </summary>
internal static class Pkcs11ProviderHelpers
{
    public static IPkcs11Slot ResolveSlot(IPkcs11Library library, Pkcs11ProviderOptionsBase options)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(options);

        if (options.SlotId is { } slotId)
        {
            return library.GetSlot(slotId);
        }

        var slots = library.GetSlots(tokenPresentOnly: true);
        if (slots.Count == 0)
        {
            throw new InvalidOperationException(
                $"PKCS#11 module '{options.ModulePath}' has no slots with a token present.");
        }

        if (!string.IsNullOrWhiteSpace(options.TokenLabel))
        {
            var label = options.TokenLabel.Trim();
            var match = slots.FirstOrDefault(s =>
                string.Equals(s.TokenLabel, label, StringComparison.Ordinal));
            if (match is null)
            {
                throw new InvalidOperationException(
                    $"No PKCS#11 token with label '{label}' was found.");
            }

            return match;
        }

        if (slots.Count > 1 && !options.PreferFirstSlotWhenAmbiguous)
        {
            throw new InvalidOperationException(
                "Multiple PKCS#11 tokens are present; configure SlotId or TokenLabel, " +
                "or set PreferFirstSlotWhenAmbiguous for development.");
        }

        return slots[0];
    }

    /// <summary>
    /// Resolves a slot that can list at least one certificate after login.
    /// Used when virtual readers report multiple token-present slots (eToken / Aladdin).
    /// </summary>
    public static IPkcs11Slot ResolveSlotWithCertificates(
        IPkcs11Library library,
        Pkcs11ProviderOptionsBase options,
        ISigningSecretProvider? secretProvider,
        Pkcs11ObjectFilter? certificateFilter,
        ref ulong? cachedSlotId)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(options);

        if (cachedSlotId is { } remembered)
        {
            return library.GetSlot(remembered);
        }

        if (options.SlotId is not null || !string.IsNullOrWhiteSpace(options.TokenLabel))
        {
            var configured = ResolveSlot(library, options);
            cachedSlotId = configured.SlotId;
            return configured;
        }

        var slots = library.GetSlots(tokenPresentOnly: true);
        if (slots.Count == 0)
        {
            throw new InvalidOperationException(
                $"PKCS#11 module '{options.ModulePath}' has no slots with a token present.");
        }

        if (slots.Count == 1)
        {
            cachedSlotId = slots[0].SlotId;
            return slots[0];
        }

        if (!options.PreferFirstSlotWhenAmbiguous)
        {
            throw new InvalidOperationException(
                "Multiple PKCS#11 tokens are present; configure SlotId or TokenLabel, " +
                "or set PreferFirstSlotWhenAmbiguous for development.");
        }

        var pin = ResolvePin(options, secretProvider);
        foreach (var slot in slots)
        {
            try
            {
                using var session = slot.OpenSession(readWrite: false);
                session.Login(pin);
                try
                {
                    if (session.FindCertificates(certificateFilter).Count > 0)
                    {
                        cachedSlotId = slot.SlotId;
                        return slot;
                    }
                }
                finally
                {
                    if (session.IsLoggedIn)
                    {
                        session.Logout();
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Empty virtual readers / login failures — try the next slot.
            }
        }

        cachedSlotId = slots[0].SlotId;
        return slots[0];
    }

    public static string ResolvePin(Pkcs11ProviderOptionsBase options, ISigningSecretProvider? secretProvider)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.PinSecretName))
        {
            throw new InvalidOperationException(
                "PinSecretName must be configured. Token PINs must not be embedded in provider options.");
        }

        if (secretProvider is null)
        {
            throw new InvalidOperationException(
                "PinSecretName is configured but no ISigningSecretProvider was supplied.");
        }

        var pin = secretProvider.GetSecret(options.PinSecretName.Trim());
        if (string.IsNullOrEmpty(pin))
        {
            throw new InvalidOperationException(
                $"PIN secret '{options.PinSecretName.Trim()}' was not found or empty.");
        }

        return pin;
    }

    public static string SanitizeFailure(Exception ex) =>
        ex switch
        {
            ObjectDisposedException => "PKCS#11 provider or session has been disposed.",
            InvalidOperationException => "PKCS#11 provider operation failed (module, slot, login, or object lookup).",
            ArgumentException => "PKCS#11 provider configuration is invalid.",
            IOException => "Failed to access the PKCS#11 module.",
            _ => "PKCS#11 provider is unavailable."
        };
}
