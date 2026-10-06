// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Telemetry;

namespace Azure.Functions.Cli.Tests.Telemetry;

public class CliTelemetryEnvironmentTests
{
    [Fact]
    public void Apply_DisablesSdkTelemetryAndPreservesOriginalSettingsForConsumers()
    {
        var environment = new InMemoryProcessEnvironment();
        environment.Set(CliTelemetryEnvironment.SdkStatsDisabled, "false");
        environment.Set(CliTelemetryEnvironment.ResourceMetricsEnabled, "true");
        environment.Set("UNRELATED", "unchanged");
        using var scope = new CliTelemetryEnvironment(environment, environment.Set);
        var telemetryScope = (ITelemetryEnvironmentScope)scope;

        telemetryScope.Apply();
        telemetryScope.Apply();

        environment.Get(CliTelemetryEnvironment.SdkStatsDisabled).Should().Be("true");
        environment.Get(CliTelemetryEnvironment.StatsbeatDisabled).Should().Be("true");
        environment.Get(CliTelemetryEnvironment.ResourceMetricsEnabled).Should().Be("false");
        scope.Get(CliTelemetryEnvironment.SdkStatsDisabled).Should().Be("false");
        scope.Get(CliTelemetryEnvironment.StatsbeatDisabled).Should().BeNull();
        scope.Get(CliTelemetryEnvironment.ResourceMetricsEnabled).Should().Be("true");
        scope.Get("UNRELATED").Should().Be("unchanged");
    }

    [Fact]
    public void Dispose_RestoresValuesAndUnsetsOriginallyMissingVariables()
    {
        var environment = new InMemoryProcessEnvironment();
        environment.Set(CliTelemetryEnvironment.ResourceMetricsEnabled, "true");
        var scope = new CliTelemetryEnvironment(environment, environment.Set);
        ((ITelemetryEnvironmentScope)scope).Apply();

        scope.Dispose();
        scope.Dispose();

        environment.Get(CliTelemetryEnvironment.SdkStatsDisabled).Should().BeNull();
        environment.Get(CliTelemetryEnvironment.StatsbeatDisabled).Should().BeNull();
        environment.Get(CliTelemetryEnvironment.ResourceMetricsEnabled).Should().Be("true");
    }

    [Fact]
    public void RestoreInheritedVariables_DoesNotMutateCurrentProcessOverrides()
    {
        var environment = new InMemoryProcessEnvironment();
        environment.Set(CliTelemetryEnvironment.SdkStatsDisabled, "false");
        using var scope = new CliTelemetryEnvironment(environment, environment.Set);
        var telemetryScope = (ITelemetryEnvironmentScope)scope;
        telemetryScope.Apply();
        var child = new Dictionary<string, string?>
        {
            [CliTelemetryEnvironment.SdkStatsDisabled] = "true",
            [CliTelemetryEnvironment.StatsbeatDisabled] = "true",
            [CliTelemetryEnvironment.ResourceMetricsEnabled] = "false",
            ["UNRELATED"] = "unchanged",
        };

        telemetryScope.RestoreInheritedVariables(child);

        child.Should().HaveCount(2);
        child[CliTelemetryEnvironment.SdkStatsDisabled].Should().Be("false");
        child["UNRELATED"].Should().Be("unchanged");
        environment.Get(CliTelemetryEnvironment.SdkStatsDisabled).Should().Be("true");
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        var environment = new InMemoryProcessEnvironment();

        FluentActions.Invoking(() => new CliTelemetryEnvironment(null!, environment.Set))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new CliTelemetryEnvironment(environment, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Members_RejectInvalidArguments()
    {
        var environment = new InMemoryProcessEnvironment();
        using var scope = new CliTelemetryEnvironment(environment, environment.Set);
        var telemetryScope = (ITelemetryEnvironmentScope)scope;

        FluentActions.Invoking(() => scope.Get(string.Empty)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => telemetryScope.RestoreInheritedVariables(null!)).Should().Throw<ArgumentNullException>();
    }
}
