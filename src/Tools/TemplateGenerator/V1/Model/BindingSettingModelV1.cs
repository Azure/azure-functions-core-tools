// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record BindingSettingModelV1
{
    public required string Name { get; init; }

    public required BindingSettingValueKindV1 Value { get; init; }

    public bool? Required { get; init; }

    public string? Label { get; init; }

    public string? Help { get; init; }

    public SettingDefaultValueModelV1? DefaultValue { get; init; }

    public string? Placeholder { get; init; }

    public string? Resource { get; init; }

    public required EquatableArray<BindingEnumValueModelV1> Enum { get; init; }

    public required EquatableArray<BindingValidatorModelV1> Validators { get; init; }
}
