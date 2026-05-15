// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

internal sealed record TemplateJobModelV2
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public required EquatableArray<TemplateInputModelV2> Inputs { get; init; }

    public required EquatableArray<string> ActionRefs { get; init; }
}
