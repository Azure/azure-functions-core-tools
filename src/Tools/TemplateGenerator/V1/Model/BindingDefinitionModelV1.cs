// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record BindingDefinitionModelV1
{
    public required string Type { get; init; }

    public required string DisplayName { get; init; }

    public required BindingDirectionKindV1 Direction { get; init; }

    public required bool EnabledInTryMode { get; init; }

    public string? Documentation { get; init; }

    public required EquatableArray<BindingSettingModelV1> Settings { get; init; }

    public required EquatableArray<BindingActionModelV1> Actions { get; init; }

    public BindingExtensionRefModelV1? Extension { get; init; }

    public required EquatableArray<BindingRuleModelV1> Rules { get; init; }
}
