// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

internal sealed record TemplateModelV2
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string Author { get; init; }

    public required string ProgrammingModel { get; init; }

    public required string Language { get; init; }

    public required EquatableArray<FileModelV2> Files { get; init; }

    public required EquatableArray<TemplateJobModelV2> Jobs { get; init; }

    public required EquatableArray<TemplateActionModelV2> Actions { get; init; }
}
