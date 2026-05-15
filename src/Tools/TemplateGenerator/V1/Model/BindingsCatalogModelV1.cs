// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record BindingsCatalogModelV1
{
    public required EquatableArray<VariableEntryModelV1> Variables { get; init; }

    public required EquatableArray<BindingDefinitionModelV1> Bindings { get; init; }
}
