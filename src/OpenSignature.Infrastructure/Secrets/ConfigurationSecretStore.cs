using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Abstractions.Secrets;
using OpenSignature.Application.Security;

namespace OpenSignature.Infrastructure.Secrets;

/// <summary>
/// Reads secrets from <see cref="SecretStoreOptions"/> (user-secrets / configuration).
/// Colon-separated names that do not bind into the options dictionary are resolved from
/// <c>Secrets:Values:{name}</c> on <see cref="IConfiguration"/>.
/// Never logs secret values.
/// </summary>
public sealed class ConfigurationSecretStore : ISecretStore
{
    private readonly IOptionsMonitor<SecretStoreOptions> _options;
    private readonly IConfiguration? _configuration;

    public ConfigurationSecretStore(IOptionsMonitor<SecretStoreOptions> options)
        : this(options, configuration: null)
    {
    }

    public ConfigurationSecretStore(
        IOptionsMonitor<SecretStoreOptions> options,
        IConfiguration? configuration)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _configuration = configuration;
    }

    /// <inheritdoc />
    public ValueTask<string?> GetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        var name = secretName.Trim();
        var values = _options.CurrentValue.Values;
        if (values is not null && values.TryGetValue(name, out var fromOptions))
        {
            return ValueTask.FromResult<string?>(fromOptions);
        }

        var fromConfiguration = _configuration?[$"{SecretStoreOptions.SectionName}:Values:{name}"];
        return ValueTask.FromResult(string.IsNullOrEmpty(fromConfiguration) ? null : fromConfiguration);
    }
}
