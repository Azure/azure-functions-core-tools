// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record BindingActionModelV1
{
    public required string Template { get; init; }

    public required string Binding { get; init; }

    public required EquatableArray<string> Settings { get; init; }
}
