// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Tests.Projects;

public class DescriptorTests
{
    [Fact]
    public void StackEquality_UsesOnlyTheCaseInsensitiveCliId()
    {
        StackDescriptor stable = new("comet", "native", "Comet");
        StackDescriptor preview = new("CoMeT", "comet-worker", "Comet Preview", "New description");
        StackDescriptor other = new("other", "native", "Comet");

        (stable == preview).Should().BeTrue();
        (stable != other).Should().BeTrue();
        new HashSet<StackDescriptor> { stable, preview, other }.Should().HaveCount(2);
    }
}
