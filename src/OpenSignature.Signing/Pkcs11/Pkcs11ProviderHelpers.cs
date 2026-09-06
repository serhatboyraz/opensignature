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

        if (slots.Count > 1)
        {
            throw new InvalidOperationException(
                "Multiple PKCS#11 tokens are present; configure SlotId or TokenLabel.");
        }

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
