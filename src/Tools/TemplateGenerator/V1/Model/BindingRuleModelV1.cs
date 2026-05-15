// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record BindingRuleModelV1
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public string? Label { get; init; }

    public string? Help { get; init; }

    public required EquatableArray<BindingRuleValueModelV1> Values { get; init; }
}
