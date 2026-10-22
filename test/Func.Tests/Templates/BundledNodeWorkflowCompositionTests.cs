// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    private static NewExecutionRequest NodeComposedItem(string target, string? reference) =>
        new(WorkingDirectory.FromExplicit(target), reference, null, null, false, true, false, false, false, [], new HashSet<string>());
}
