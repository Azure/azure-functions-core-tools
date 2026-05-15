// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Tools.TemplateGenerator.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.Model.V1;

internal sealed record TemplateModelV1
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Runtime { get; init; }

    public required EquatableArray<FileModelV1> Files { get; init; }

    public required FunctionModelV1 Function { get; init; }

    public required MetadataModelV1 Metadata { get; init; }
}

internal sealed record FileModelV1(string Name, string Content) : IEquatable<FileModelV1>;

internal sealed record FunctionModelV1
{
    public required EquatableArray<BindingModelV1> Bindings { get; init; }

    public string? ScriptFile { get; init; }

    public bool? Disabled { get; init; }

    public string? EntryPoint { get; init; }
}

internal enum BindingDirectionKindV1
{
    In,
    Out,
}

internal sealed record BindingModelV1
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public required BindingDirectionKindV1 Direction { get; init; }

    public required EquatableArray<BindingExtensionEntryV1> ExtensionData { get; init; }
}

internal sealed record BindingExtensionEntryV1(string Key, BindingExtensionValueV1 Value) : IEquatable<BindingExtensionEntryV1>;

internal enum BindingExtensionValueKindV1
{
    String,
    Bool,
    StringArray,
}

internal sealed record BindingExtensionValueV1(BindingExtensionValueKindV1 Kind, string? StringValue, bool? BoolValue, EquatableArray<string>? ArrayValue)
    : IEquatable<BindingExtensionValueV1>;

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
