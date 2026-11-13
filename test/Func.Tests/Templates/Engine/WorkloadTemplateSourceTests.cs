// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class WorkloadTemplateSourceTests
{
    [Fact]
    public async Task GetRootsAsync_ReturnsNoRoots()
    {
        WorkloadTemplateSource source = new();
        IReadOnlyList<WorkloadTemplateRoot> roots = await source.GetRootsAsync(CancellationToken.None);
        roots.Should().BeEmpty();
    }
}
