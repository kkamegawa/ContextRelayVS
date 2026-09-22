using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceHub.Framework;

namespace ContextRelay.VisualStudio;

/// <summary>Provides the language selected for the Visual Studio user interface.</summary>
public interface IVisualStudioLanguageService
{
    Task<int> GetUiLocaleAsync(CancellationToken cancellationToken);
}

/// <summary>Shared broker and language-selection helpers used by both extension processes.</summary>
public static class VisualStudioLanguageService
{
    public const string ServiceName = "ContextRelay.VisualStudioLanguage";
    public const string ServiceVersion = "1.0";

    public static readonly ServiceJsonRpcDescriptor<IVisualStudioLanguageService> Descriptor =
        new(
            new ServiceMoniker(ServiceName, new Version(ServiceVersion)),
            ServiceJsonRpcDescriptor.Formatters.UTF8SystemTextJson,
            ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);

    public static string ResolveLanguage(string? configuredLanguage, int? hostLcid)
    {
        var normalized = (configuredLanguage ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "en" or "en-us" or "en-gb" => "en",
            "ja" or "ja-jp" => "ja",
            "" or "auto" => IsJapanese(hostLcid) ? "ja" : "en",
            _ => "en",
        };
    }

    private static bool IsJapanese(int? hostLcid)
    {
        if (!hostLcid.HasValue)
        {
            return false;
        }

        try
        {
            return string.Equals(
                CultureInfo.GetCultureInfo(hostLcid.Value).TwoLetterISOLanguageName,
                "ja",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
