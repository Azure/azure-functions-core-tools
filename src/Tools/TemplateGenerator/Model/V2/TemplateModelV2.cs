// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.Model.V2;

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

internal sealed record FileModelV2(string Name, string Content) : IEquatable<FileModelV2>;

internal sealed record TemplateJobModelV2
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public required EquatableArray<TemplateInputModelV2> Inputs { get; init; }

    public required EquatableArray<string> ActionRefs { get; init; }
}

internal sealed record TemplateInputModelV2
{
    public required string AssignTo { get; init; }

    public required string ParamId { get; init; }

    public string? DefaultValue { get; init; }

    public required bool Required { get; init; }

    public TemplateInputConditionModelV2? Condition { get; init; }
}

internal sealed record TemplateInputConditionModelV2
{
    public required string Name { get; init; }

    public required EquatableArray<string> Values { get; init; }

    public required string Operator { get; init; }
}

internal sealed record TemplateActionModelV2
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public required EquatableArray<ActionExtensionEntryV2> ExtensionData { get; init; }
}

internal sealed record ActionExtensionEntryV2(string Key, ActionExtensionValueV2 Value) : IEquatable<ActionExtensionEntryV2>;

internal enum ActionExtensionValueKindV2
{
    String,
    Bool,
    StringArray,
    Number,
    Null,
}

internal sealed record ActionExtensionValueV2(
    ActionExtensionValueKindV2 Kind,
    string? StringValue,
    bool? BoolValue,
    double? NumberValue,
    EquatableArray<string>? ArrayValue)
    : IEquatable<ActionExtensionValueV2>;
