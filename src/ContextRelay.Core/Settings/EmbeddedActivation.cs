using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ContextRelay.Core.Auth;

namespace ContextRelay.Core.Settings;

/// <summary>
/// Describes whether SharePoint Embedded retrieval is active.
/// </summary>
public enum EmbeddedActivationStatus
{
    /// <summary>Embedded retrieval is active.</summary>
    Active,

    /// <summary><see cref="ContextRelaySettingsSnapshot.EmbeddedEnabled"/> is off.</summary>
    Disabled,

    /// <summary>The cloud is not Global, where the Retrieval API is not available.</summary>
    UnsupportedCloud,

    /// <summary>No enabled source passes validation.</summary>
    NoValidSources,
}

/// <summary>
/// The result of evaluating the Embedded activation conditions.
/// </summary>
public sealed class EmbeddedActivationResult
{
    internal EmbeddedActivationResult(EmbeddedActivationStatus status, IReadOnlyList<EmbeddedSourceSettings> validSources, int invalidSourceCount)
    {
        Status = status;
        ValidSources = validSources;
        InvalidSourceCount = invalidSourceCount;
    }

    /// <summary>Gets the activation status.</summary>
    public EmbeddedActivationStatus Status { get; }

    /// <summary>Gets a value indicating whether Embedded retrieval is active.</summary>
    public bool IsActive => Status == EmbeddedActivationStatus.Active;

    /// <summary>Gets the enabled, valid sources in configured order, normalized.</summary>
    public IReadOnlyList<EmbeddedSourceSettings> ValidSources { get; }

    /// <summary>Gets the number of enabled entries skipped because they are invalid.</summary>
    public int InvalidSourceCount { get; }

    /// <summary>
    /// Resolves the source to search: the selected source when it is still valid, otherwise the only
    /// valid source when there is exactly one.
    /// </summary>
    /// <param name="selectedContainerTypeId">The container type identifier selected in this session, if any.</param>
    /// <returns>The source to search, or <see langword="null"/> when the user must choose.</returns>
    public EmbeddedSourceSettings? ResolveSource(string? selectedContainerTypeId)
    {
        if (EmbeddedSourceValidator.TryNormalizeContainerTypeId(selectedContainerTypeId, out var selected))
        {
            var match = ValidSources.FirstOrDefault(source => string.Equals(source.ContainerTypeId, selected, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return ValidSources.Count == 1 ? ValidSources[0] : null;
    }
}

/// <summary>
/// Evaluates the SharePoint Embedded activation conditions shared by the router suggestions and /embedded.
/// </summary>
public static class EmbeddedActivation
{
    /// <summary>
    /// Evaluates the activation conditions: Embedded enabled, the Global cloud, and at least one enabled valid source.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <returns>The activation result.</returns>
    public static EmbeddedActivationResult Evaluate(ContextRelaySettingsSnapshot settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var entries = EmbeddedSourceValidator.Validate(settings.EmbeddedSources);
        var validSources = entries
            .Where(entry => entry.IsValid && entry.Normalized.Enabled)
            .Select(entry => entry.Normalized)
            .ToArray();
        var invalidCount = entries.Count(entry => !entry.IsValid && entry.Normalized.Enabled);

        EmbeddedActivationStatus status;
        if (!settings.EmbeddedEnabled)
        {
            status = EmbeddedActivationStatus.Disabled;
        }
        else if (settings.CloudEnvironment != CloudEnvironment.Global)
        {
            status = EmbeddedActivationStatus.UnsupportedCloud;
        }
        else if (validSources.Length == 0)
        {
            status = EmbeddedActivationStatus.NoValidSources;
        }
        else
        {
            status = EmbeddedActivationStatus.Active;
        }

        return new EmbeddedActivationResult(status, validSources, invalidCount);
    }

    /// <summary>
    /// Builds a value that changes whenever a setting that invalidates Embedded results changes:
    /// the client, tenant, cloud and endpoints, and the Embedded settings.
    /// </summary>
    /// <param name="settings">The current settings.</param>
    /// <returns>The fingerprint.</returns>
    public static string BuildInvalidationFingerprint(ContextRelaySettingsSnapshot settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var builder = new StringBuilder()
            .Append(settings.ClientId).Append('\n')
            .Append(settings.TenantId).Append('\n')
            .Append(settings.CloudEnvironment).Append('\n')
            .Append(settings.CustomGraphEndpoint).Append('\n')
            .Append(settings.CustomAuthEndpoint).Append('\n')
            .Append(settings.EmbeddedEnabled).Append('\n');
        foreach (var source in settings.EmbeddedSources)
        {
            builder
                .Append(source?.Name).Append('\t')
                .Append(source?.ContainerTypeId).Append('\t')
                .Append(source?.Enabled).Append('\n');
        }

        return builder.ToString();
    }
}
