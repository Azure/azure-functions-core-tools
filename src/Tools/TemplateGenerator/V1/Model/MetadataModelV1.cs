// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record MetadataModelV1
{
    public required string DefaultFunctionName { get; init; }

    public required string Description { get; init; }

    public required string Name { get; init; }

    public required string Language { get; init; }

    public required EquatableArray<string> Category { get; init; }

    public required string CategoryStyle { get; init; }

    public required bool EnabledInTryMode { get; init; }

    public EquatableArray<string>? UserPrompt { get; init; }

    public EquatableArray<string>? Filters { get; init; }

    public string? Trigger { get; init; }
}
