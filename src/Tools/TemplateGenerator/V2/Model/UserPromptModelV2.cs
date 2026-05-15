// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

internal sealed record UserPromptModelV2
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required UserPromptValueKindV2 Value { get; init; }

    public UserPromptDefaultValueModelV2? DefaultValue { get; init; }

    public string? Label { get; init; }

    public string? Help { get; init; }

    public string? Placeholder { get; init; }

    public string? Resource { get; init; }

    public string? FileExtension { get; init; }

    public required EquatableArray<UserPromptValidatorModelV2> Validators { get; init; }

    public required EquatableArray<UserPromptEnumValueModelV2> Enum { get; init; }
}
