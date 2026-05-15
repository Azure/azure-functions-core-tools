// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

internal sealed record TemplateInputModelV2
{
    public required string AssignTo { get; init; }

    public required string ParamId { get; init; }

    public string? DefaultValue { get; init; }

    public required bool Required { get; init; }

    public TemplateInputConditionModelV2? Condition { get; init; }
}
