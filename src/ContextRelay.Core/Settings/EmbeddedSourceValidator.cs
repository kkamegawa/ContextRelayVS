using System;
using System.Collections.Generic;

namespace ContextRelay.Core.Settings;

/// <summary>
/// Identifies why a configured SharePoint Embedded source entry is invalid.
/// </summary>
public enum EmbeddedSourceError
{
    /// <summary>The entry is valid.</summary>
    None,

    /// <summary>The display name is empty after trimming.</summary>
    NameRequired,

    /// <summary>The display name is longer than <see cref="EmbeddedSourceValidator.MaxNameLength"/> characters.</summary>
    NameTooLong,

    /// <summary>Another entry already uses the same display name, ignoring case.</summary>
    NameDuplicate,

    /// <summary>The container type identifier is not a GUID.</summary>
    ContainerTypeIdInvalid,

    /// <summary>Another entry already uses the same container type identifier.</summary>
    ContainerTypeIdDuplicate,
}

/// <summary>
/// The validation result for one configured source entry.
/// </summary>
public sealed class EmbeddedSourceValidationEntry
{
    internal EmbeddedSourceValidationEntry(int index, EmbeddedSourceSettings normalized, EmbeddedSourceError nameError, EmbeddedSourceError containerTypeIdError)
    {
        Index = index;
        Normalized = normalized;
        NameError = nameError;
        ContainerTypeIdError = containerTypeIdError;
    }

    /// <summary>Gets the position of the entry in the configured list.</summary>
    public int Index { get; }

    /// <summary>Gets the entry with its name trimmed and its container type identifier in lowercase D format when valid.</summary>
    public EmbeddedSourceSettings Normalized { get; }

    /// <summary>Gets the display name error, or <see cref="EmbeddedSourceError.None"/>.</summary>
    public EmbeddedSourceError NameError { get; }

    /// <summary>Gets the container type identifier error, or <see cref="EmbeddedSourceError.None"/>.</summary>
    public EmbeddedSourceError ContainerTypeIdError { get; }

    /// <summary>Gets a value indicating whether the entry has no errors.</summary>
    public bool IsValid => NameError == EmbeddedSourceError.None && ContainerTypeIdError == EmbeddedSourceError.None;
}

/// <summary>
/// Validates SharePoint Embedded source entries. The Options page and the runtime share these rules.
/// </summary>
public static class EmbeddedSourceValidator
{
    /// <summary>The maximum display name length after trimming.</summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// Validates every entry. A duplicate is reported on the later entry, so the first one stays valid.
    /// </summary>
    /// <param name="sources">The configured entries.</param>
    /// <returns>One result per entry, in order.</returns>
    public static IReadOnlyList<EmbeddedSourceValidationEntry> Validate(IReadOnlyList<EmbeddedSourceSettings>? sources)
    {
        var results = new List<EmbeddedSourceValidationEntry>();
        if (sources is null)
        {
            return results;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var containerTypeIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index] ?? new EmbeddedSourceSettings();
            var name = (source.Name ?? string.Empty).Trim();
            var nameError = GetNameError(name);
            if (nameError == EmbeddedSourceError.None && !names.Add(name))
            {
                nameError = EmbeddedSourceError.NameDuplicate;
            }

            var containerTypeIdError = EmbeddedSourceError.None;
            var containerTypeId = (source.ContainerTypeId ?? string.Empty).Trim();
            if (TryNormalizeContainerTypeId(containerTypeId, out var normalizedId))
            {
                containerTypeId = normalizedId;
                if (!containerTypeIds.Add(containerTypeId))
                {
                    containerTypeIdError = EmbeddedSourceError.ContainerTypeIdDuplicate;
                }
            }
            else
            {
                containerTypeIdError = EmbeddedSourceError.ContainerTypeIdInvalid;
            }

            var normalized = new EmbeddedSourceSettings
            {
                Name = name,
                ContainerTypeId = containerTypeId,
                Enabled = source.Enabled,
            };
            results.Add(new EmbeddedSourceValidationEntry(index, normalized, nameError, containerTypeIdError));
        }

        return results;
    }

    /// <summary>
    /// Gets the display name error for a single name, without the duplicate check.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <returns>The error, or <see cref="EmbeddedSourceError.None"/>.</returns>
    public static EmbeddedSourceError GetNameError(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return EmbeddedSourceError.NameRequired;
        }

        return trimmed.Length > MaxNameLength ? EmbeddedSourceError.NameTooLong : EmbeddedSourceError.None;
    }

    /// <summary>
    /// Normalizes a container type identifier to a lowercase GUID in D format.
    /// </summary>
    /// <param name="value">The input value.</param>
    /// <param name="normalized">The normalized value, or an empty string when the input is not a GUID.</param>
    /// <returns><see langword="true"/> when the input is a GUID.</returns>
    public static bool TryNormalizeContainerTypeId(string? value, out string normalized)
    {
        if (Guid.TryParse((value ?? string.Empty).Trim(), out var guid))
        {
            normalized = guid.ToString("D").ToLowerInvariant();
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}
