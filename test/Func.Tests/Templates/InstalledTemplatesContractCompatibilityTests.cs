// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class InstalledTemplatesContractCompatibilityTests
{
    [Fact]
    public async Task ChannelOverload_LegacyImplementation_DelegatesWithoutChangingRowsOrToken()
    {
        using CancellationTokenSource cancellation = new();
        LegacyTemplates legacy = new();
        IInstalledTemplatesWorkloads contract = legacy;

        IReadOnlyList<InstalledTemplatesWorkload> rows = await contract.ListInstalledAsync("node", cancellation.Token, BundleChannel.Preview);

        rows.Should().BeSameAs(legacy.Rows);
        legacy.Stack.Should().Be("node");
        legacy.Token.Should().Be(cancellation.Token);
    }

    private sealed class LegacyTemplates : IInstalledTemplatesWorkloads
    {
        public IReadOnlyList<InstalledTemplatesWorkload> Rows { get; } = [new("node", "1.0.0", "templates")];

        public string? Stack { get; private set; }

        public CancellationToken Token { get; private set; }

        public Task<IReadOnlyList<InstalledTemplatesWorkload>> ListInstalledAsync(string stack, CancellationToken cancellationToken = default)
        {
            Stack = stack;
            Token = cancellationToken;
            return Task.FromResult(Rows);
        }
    }
}