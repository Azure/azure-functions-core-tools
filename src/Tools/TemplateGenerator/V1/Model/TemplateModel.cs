// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

internal sealed record TemplateModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Runtime { get; init; }

    public required EquatableArray<FileModel> Files { get; init; }

    public required FunctionModel Function { get; init; }

    public required MetadataModel Metadata { get; init; }
}

internal sealed record FileModel(string Name, string Content) : IEquatable<FileModel>;

internal sealed record FunctionModel
{
    public required EquatableArray<BindingModel> Bindings { get; init; }

    public string? ScriptFile { get; init; }

    public bool? Disabled { get; init; }

    public string? EntryPoint { get; init; }
}

internal enum BindingDirectionKind
{
    In,
    Out,
}

internal sealed record BindingModel
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public required BindingDirectionKind Direction { get; init; }

    public required EquatableArray<BindingExtensionEntry> ExtensionData { get; init; }
}

internal sealed record BindingExtensionEntry(string Key, BindingExtensionValue Value) : IEquatable<BindingExtensionEntry>;

internal enum BindingExtensionValueKind
{
    String,
    Bool,
    StringArray,
}

internal sealed record BindingExtensionValue(BindingExtensionValueKind Kind, string? StringValue, bool? BoolValue, EquatableArray<string>? ArrayValue)
    : IEquatable<BindingExtensionValue>;

internal sealed record MetadataModel
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
