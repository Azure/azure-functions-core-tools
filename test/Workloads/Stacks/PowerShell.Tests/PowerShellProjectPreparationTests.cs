// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;

namespace Azure.Functions.Cli.Workloads.PowerShell.Tests;

public sealed class PowerShellProjectPreparationTests
{
    [Fact]
    public async Task PrepareManagedDependenciesAsync_Disabled_DoesNotResolveOrReadSource()
    {
        ControlledPreparation preparation = new();

        PowerShellManagedDependencyContent result = await preparation.PrepareManagedDependenciesAsync(false, CancellationToken.None);

        result.Content.Should().BeNull();
        result.SourceIdentity.Should().BeNull();
        result.SourceSha256.Should().BeNull();
        result.Diagnostics.Should().BeEmpty();
        preparation.Lookups.Should().Be(0);
        preparation.SourceReads.Should().Be(0);
    }

    [Fact]
    public async Task PrepareManagedDependenciesAsync_ResolvedMajor_ExpandsOwnedContentAndPinsOriginalSource()
    {
        ControlledPreparation preparation = new() { Major = "12" };
        string source = new PowerShellProjectPreparation().ReadRequirementsTemplate();
        using CancellationTokenSource cancellation = new();

        PowerShellManagedDependencyContent result = await preparation.PrepareManagedDependenciesAsync(true, cancellation.Token);

        result.Content.Should().Be(source.Replace("MAJOR_VERSION", "12", StringComparison.Ordinal));
        result.Content.Should().Contain("# 'Az' = '12.*'");
        result.SourceIdentity.Should().Be(PowerShellProjectPreparation.RequirementsSourceIdentity);
        result.SourceSha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
        result.SourceSha256.Should().NotBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result.Content!))));
        result.Diagnostics.Should().BeEmpty();
        preparation.Token.Should().Be(cancellation.Token);
        preparation.Lookups.Should().Be(1);
        preparation.SourceReads.Should().Be(1);
    }

    [Fact]
    public async Task PrepareManagedDependenciesAsync_UnresolvedMajor_PreservesCommentedPlaceholderWithDiagnostic()
    {
        ControlledPreparation preparation = new();

        PowerShellManagedDependencyContent result = await preparation.PrepareManagedDependenciesAsync(true, CancellationToken.None);

        result.Content.Should().Be(new PowerShellProjectPreparation().ReadRequirementsTemplate());
        result.Content.Should().Contain("# 'Az' = 'MAJOR_VERSION.*'");
        result.Diagnostics.Should().ContainSingle().Which.Should().Contain("Could not resolve", "commented", "MAJOR_VERSION");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareManagedDependenciesAsync_PreCancelled_PerformsNoLookupOrRead(bool enabled)
    {
        ControlledPreparation preparation = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> action = () => preparation.PrepareManagedDependenciesAsync(enabled, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        preparation.Lookups.Should().Be(0);
        preparation.SourceReads.Should().Be(0);
    }

    [Fact]
    public async Task PrepareManagedDependenciesAsync_LegacyLookupSwallowsCancellation_StillPropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new();
        ControlledPreparation preparation = new() { DuringLookup = cancellation.Cancel };

        Func<Task> action = () => preparation.PrepareManagedDependenciesAsync(true, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class ControlledPreparation : PowerShellProjectPreparation
    {
        public string? Major { get; init; }
        public Action? DuringLookup { get; init; }
        public int Lookups { get; private set; }
        public int SourceReads { get; private set; }
        public CancellationToken Token { get; private set; }

        public override string ReadRequirementsTemplate()
        {
            SourceReads++;
            return base.ReadRequirementsTemplate();
        }

        public override Task<string?> GetLatestAzModuleMajorVersionAsync(CancellationToken cancellationToken)
        {
            Lookups++;
            Token = cancellationToken;
            DuringLookup?.Invoke();
            return Task.FromResult(Major);
        }
    }
}
