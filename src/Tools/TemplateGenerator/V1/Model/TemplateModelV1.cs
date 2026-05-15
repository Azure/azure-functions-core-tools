// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record TemplateModelV1
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Runtime { get; init; }

    public required EquatableArray<FileModelV1> Files { get; init; }

    public required FunctionModelV1 Function { get; init; }

    public required MetadataModelV1 Metadata { get; init; }
}
