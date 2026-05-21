// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles.Tests;

internal sealed class FakeInstalledBundleWorkloads(IReadOnlyList<InstalledBundleWorkload> rows) : IInstalledBundleWorkloads
{
    public Task<IReadOnlyList<InstalledBundleWorkload>> ListInstalledAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(rows);
}
