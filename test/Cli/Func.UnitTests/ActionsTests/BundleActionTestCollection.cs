// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    // Ensures bundle action tests don't run in parallel since they mutate global state
    // such as Environment.CurrentDirectory and ColoredConsole output writers.
    [CollectionDefinition("BundleActionTests", DisableParallelization = true)]
    public class BundleActionTestCollection
    {
    }
}
