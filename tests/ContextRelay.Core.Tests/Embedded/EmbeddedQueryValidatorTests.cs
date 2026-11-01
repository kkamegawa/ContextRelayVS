using System.Collections.Generic;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.Auth;
using ContextRelay.Core.Embedded;
using ContextRelay.Core.Settings;
using Xunit;

namespace ContextRelay.Core.Tests.Embedded;

public sealed class EmbeddedQueryValidatorTests
{
    private const string LegalId = "4a9c1d2e-0b3f-4c5d-8e6f-7a8b9c0d1e2f";
    private const string FinanceId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Validate_WhenDisabled_RejectsBeforeCheckingTheQuery()
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(enabled: false, Source("Legal", LegalId)));

        var result = EmbeddedQueryValidator.Validate(activation, null, string.Empty, 10);

        Assert.Equal(EmbeddedQueryError.NotActive, result.Error);
        Assert.Equal(EmbeddedActivationStatus.Disabled, result.ActivationStatus);
    }

    [Theory]
    [InlineData(CloudEnvironment.USGovGCCHigh)]
    [InlineData(CloudEnvironment.USGovDoD)]
    [InlineData(CloudEnvironment.China)]
    [InlineData(CloudEnvironment.Custom)]
    public void Validate_WhenCloudIsNotGlobal_ReportsUnsupportedCloud(CloudEnvironment cloud)
    {
        var settings = CreateSettings(enabled: true, Source("Legal", LegalId));
        settings.CloudEnvironment = cloud;

        var result = EmbeddedQueryValidator.Validate(EmbeddedActivation.Evaluate(settings), null, "renewal", 10);

        Assert.Equal(EmbeddedQueryError.NotActive, result.Error);
        Assert.Equal(EmbeddedActivationStatus.UnsupportedCloud, result.ActivationStatus);
    }

    [Fact]
    public void Validate_WhenNoSourceIsValid_ReportsNoValidSources()
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(
            enabled: true,
            Source("Legal", "not-a-guid"),
            Source("Finance", FinanceId, enabled: false)));

        var result = EmbeddedQueryValidator.Validate(activation, null, "renewal", 10);

        Assert.Equal(EmbeddedQueryError.NotActive, result.Error);
        Assert.Equal(EmbeddedActivationStatus.NoValidSources, result.ActivationStatus);
        Assert.Equal(1, activation.InvalidSourceCount);
    }

    [Fact]
    public void Validate_WithOneValidSource_SelectsItAutomatically()
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(enabled: true, Source("Legal", LegalId.ToUpperInvariant())));

        var result = EmbeddedQueryValidator.Validate(activation, null, "  renewal terms  ", 10);

        Assert.True(result.IsValid);
        Assert.Equal(LegalId, result.Source!.ContainerTypeId);
        Assert.Equal("renewal terms", result.Query);
    }

    [Fact]
    public void Validate_WithSeveralSourcesAndNoSelection_RequiresSelection()
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(enabled: true, Source("Legal", LegalId), Source("Finance", FinanceId)));

        var result = EmbeddedQueryValidator.Validate(activation, null, "renewal", 10);

        Assert.Equal(EmbeddedQueryError.SourceNotSelected, result.Error);
    }

    [Fact]
    public void Validate_WithSeveralSourcesAndASelection_UsesTheSelectedSource()
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(enabled: true, Source("Legal", LegalId), Source("Finance", FinanceId)));

        var result = EmbeddedQueryValidator.Validate(activation, FinanceId.ToUpperInvariant(), "renewal", 10);

        Assert.True(result.IsValid);
        Assert.Equal("Finance", result.Source!.Name);
    }

    [Fact]
    public void Validate_WithAStaleSelection_FallsBackToSelectionRequired()
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(enabled: true, Source("Legal", LegalId), Source("Finance", FinanceId)));

        var result = EmbeddedQueryValidator.Validate(activation, "99999999-0000-0000-0000-000000000000", "renewal", 10);

        Assert.Equal(EmbeddedQueryError.SourceNotSelected, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyQuery_IsRejected(string? query)
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(enabled: true, Source("Legal", LegalId)));

        var result = EmbeddedQueryValidator.Validate(activation, null, query, 10);

        Assert.Equal(EmbeddedQueryError.QueryEmpty, result.Error);
    }

    [Fact]
    public void Validate_QueryAtTheLimitAfterTrimming_IsAccepted()
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(enabled: true, Source("Legal", LegalId)));

        var result = EmbeddedQueryValidator.Validate(activation, null, "  " + new string('a', RetrievalClient.MaxQueryLength) + "  ", 10);

        Assert.True(result.IsValid);
        Assert.Equal(RetrievalClient.MaxQueryLength, result.Query.Length);
    }

    [Fact]
    public void Validate_QueryOverTheLimit_IsRejectedWithoutTruncation()
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(enabled: true, Source("Legal", LegalId)));
        var query = new string('a', RetrievalClient.MaxQueryLength + 1);

        var result = EmbeddedQueryValidator.Validate(activation, null, query, 10);

        Assert.Equal(EmbeddedQueryError.QueryTooLong, result.Error);
        Assert.Equal(query, result.Query);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(12, 12)]
    [InlineData(50, 25)]
    public void Validate_ClampsMaxResults(int requested, int expected)
    {
        var activation = EmbeddedActivation.Evaluate(CreateSettings(enabled: true, Source("Legal", LegalId)));

        var result = EmbeddedQueryValidator.Validate(activation, null, "renewal", requested);

        Assert.Equal(expected, result.MaxResults);
    }

    [Fact]
    public void SourceValidator_ReportsNameAndIdErrorsOnTheLaterDuplicate()
    {
        var entries = EmbeddedSourceValidator.Validate(new List<EmbeddedSourceSettings>
        {
            Source(" Legal ", LegalId),
            Source("legal", LegalId.ToUpperInvariant()),
            Source(string.Empty, FinanceId),
            Source(new string('n', EmbeddedSourceValidator.MaxNameLength + 1), "{" + FinanceId + "}"),
            Source("Bad id", "123")
        });

        Assert.True(entries[0].IsValid);
        Assert.Equal("Legal", entries[0].Normalized.Name);
        Assert.Equal(EmbeddedSourceError.NameDuplicate, entries[1].NameError);
        Assert.Equal(EmbeddedSourceError.ContainerTypeIdDuplicate, entries[1].ContainerTypeIdError);
        Assert.Equal(EmbeddedSourceError.NameRequired, entries[2].NameError);
        Assert.Equal(EmbeddedSourceError.NameTooLong, entries[3].NameError);
        Assert.Equal(EmbeddedSourceError.ContainerTypeIdDuplicate, entries[3].ContainerTypeIdError);
        Assert.Equal(FinanceId, entries[3].Normalized.ContainerTypeId);
        Assert.Equal(EmbeddedSourceError.ContainerTypeIdInvalid, entries[4].ContainerTypeIdError);
    }

    [Fact]
    public void BuildInvalidationFingerprint_ChangesWhenEmbeddedOrIdentitySettingsChange()
    {
        var settings = CreateSettings(enabled: true, Source("Legal", LegalId));
        var baseline = EmbeddedActivation.BuildInvalidationFingerprint(settings);

        settings.EmbeddedSources[0].Name = "Legal 2";
        var renamed = EmbeddedActivation.BuildInvalidationFingerprint(settings);
        settings.TenantId = "contoso.example";
        var tenantChanged = EmbeddedActivation.BuildInvalidationFingerprint(settings);

        Assert.NotEqual(baseline, renamed);
        Assert.NotEqual(renamed, tenantChanged);
        Assert.Equal(tenantChanged, EmbeddedActivation.BuildInvalidationFingerprint(settings));
    }

    private static ContextRelaySettingsSnapshot CreateSettings(bool enabled, params EmbeddedSourceSettings[] sources)
    {
        return new ContextRelaySettingsSnapshot
        {
            EmbeddedEnabled = enabled,
            CloudEnvironment = CloudEnvironment.Global,
            EmbeddedSources = new List<EmbeddedSourceSettings>(sources)
        };
    }

    private static EmbeddedSourceSettings Source(string name, string containerTypeId, bool enabled = true)
    {
        return new EmbeddedSourceSettings { Name = name, ContainerTypeId = containerTypeId, Enabled = enabled };
    }
}
