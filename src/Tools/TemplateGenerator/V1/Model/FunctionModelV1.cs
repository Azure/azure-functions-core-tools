// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record FunctionModelV1
{
    public required EquatableArray<BindingModelV1> Bindings { get; init; }

    public string? ScriptFile { get; init; }

    public bool? Disabled { get; init; }

    public string? EntryPoint { get; init; }
}
