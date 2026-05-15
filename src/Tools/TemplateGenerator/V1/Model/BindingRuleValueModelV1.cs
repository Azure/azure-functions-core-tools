// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record BindingRuleValueModelV1
{
    public required string Value { get; init; }

    public required string Display { get; init; }

    public required EquatableArray<string> HiddenSettings { get; init; }

    public required EquatableArray<string> ShownSettings { get; init; }
}
