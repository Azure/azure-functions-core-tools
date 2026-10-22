// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Supplies canonical parameter values and output intent for one selected template invocation.
/// </summary>
internal sealed record TemplateInvocationRequest(WorkingDirectory OutputDirectory, IReadOnlyDictionary<string, string?> Parameters, string? Name = null, bool DryRun = false, bool Force = false)
{
    public NativeTemplateInvocationEffects? EffectSupport { get; init; }

    public NativeMsBuildBindingSnapshot? MsBuildBindings { get; init; }

    public NativeTemplateSourceSnapshot? SourceSnapshot { get; init; }

    public NativeTemplatePreparationSnapshot? ExpectedPreparation { get; init; }

    internal IReadOnlyList<TemplateEffectSource> CapturedSources { get; init; } = [];

    internal bool RequireCapturedSources { get; init; }

    internal IReadOnlySet<string> ExplicitParameterNames { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    internal TemplateArgumentParseRequest? ArgumentSource { get; init; }

    internal NativeTemplatePolicyCandidate? PolicyCandidate { get; init; }

    internal bool PolicyEffectsConfirmed { get; init; }

    internal TemplateFSharpCompileEffect? PolicyCompileRepair { get; init; }

    internal BoundNativeTemplateExecutor? OwnerExecutor { get; init; }

    internal BoundNativeProjectPreparation? ProjectPreparationBinding { get; init; }
}

internal sealed record NativeTemplateInvocationEffects(
    WorkingDirectory ProjectDirectory, string ExactProjectPath, INativeTemplateEffectCapability Capability, bool Confirmed)
{
    public TemplateFSharpCompileEffect? FSharpRepair { get; init; }
}

/// <summary>
/// Distinguishes invocation outcomes without discarding their diagnostics or reported changes.
/// </summary>
internal enum TemplateInvocationStatus
{
    Success,
    Failed,
    InvalidArguments,
    MissingRequiredParameters,
    FileConflict,
    TemplateChanged,
    TemplateUnavailable,
    InvalidTemplate,
    EngineFailure,
    ConfigurationFailure,
    EffectFailure,
}

/// <summary>
/// Identifies the supported native file operations independently of their display names.
/// </summary>
[JsonConverter(typeof(TemplateFileChangeKindJsonConverter))]
internal enum TemplateFileChangeKind
{
    Create,
    Change,
    Overwrite,
    Delete,
}

/// <summary>
/// Preserves file-change names in JSON and rejects unknown numeric values.
/// </summary>
internal sealed class TemplateFileChangeKindJsonConverter() : JsonStringEnumConverter<TemplateFileChangeKind>(allowIntegerValues: false);

/// <summary>
/// Describes a native file or directory effect relative to the requested output directory.
/// </summary>
internal sealed record TemplateFileChange(string Path, TemplateFileChangeKind Kind)
{
    public static TemplateFileChange FromNative(string path, ChangeKind kind) => new(path, kind switch
    {
        ChangeKind.Create => TemplateFileChangeKind.Create,
        ChangeKind.Change => TemplateFileChangeKind.Change,
        ChangeKind.Overwrite => TemplateFileChangeKind.Overwrite,
        ChangeKind.Delete => TemplateFileChangeKind.Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The template engine returned an unsupported file-change kind."),
    });

    public string GetKindName() => Kind switch
    {
        TemplateFileChangeKind.Create => "Create",
        TemplateFileChangeKind.Change => "Change",
        TemplateFileChangeKind.Overwrite => "Overwrite",
        TemplateFileChangeKind.Delete => "Delete",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unsupported internal template file-change kind."),
    };
}

/// <summary>
/// Reports an invocation outcome with its native changes and any user-facing diagnostic.
/// </summary>
internal sealed record TemplateInvocationResult(TemplateInvocationStatus Status, IReadOnlyList<TemplateFileChange> Changes, string? Diagnostic = null)
{
    [JsonIgnore]
    public IReadOnlyList<TemplateProjectConfigurationPlan> ProjectConfigurations { get; init; } = [];

    public IReadOnlyList<string> ConfiguredProjects { get; init; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeTemplateEffectResult? Effects { get; init; }

    [JsonIgnore]
    internal NativeTemplatePreparationSnapshot? Capture { get; init; }

    [JsonIgnore]
    internal NativeProjectPreparationCapture? ProjectPreparationCapture { get; init; }

    /// <summary>
    /// Distinguishes completed real output from a preview or a conflict plan without changing the serialized result.
    /// </summary>
    internal bool HasCommittedOutput { get; init; }
}

/// <summary>
/// Captures mounted configuration and parameter definitions for selection and later source revalidation.
/// </summary>
/// <param name="Content">Decoded mounted template.json content.</param>
/// <param name="Fingerprint">SHA-256 of the exact template.json stream bytes before text decoding.</param>
/// <param name="Type">Current mounted item or project type, or null when missing or unrecognized.</param>
/// <param name="Parameters">Native definitions loaded from the mounted configuration.</param>
internal sealed record TemplateConfigurationSnapshot(string Content, string Fingerprint, TemplateType? Type, IReadOnlyList<ITemplateParameter> Parameters)
{
    public string? DefaultName { get; init; }

    internal string? PackageConfigurationPath { get; init; }

    public string Content { get; } = Content ?? throw new ArgumentNullException(nameof(Content));

    /// <summary>
    /// Owns the parsed host view independently of any disposable parsing document.
    /// </summary>
    public JsonElement Root { get; } = ParseRoot(Content);

    /// <summary>
    /// Identifies the selected or fallback mounted host file, or null when neither exists.
    /// </summary>
    public string? HostConfigPlace { get; init; }

    /// <summary>
    /// Captures host metadata separately from the template.json fingerprint.
    /// </summary>
    public string? HostData { get; init; }

    internal static JsonElement ParseRoot(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var document = JsonDocument.Parse(content,
                new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidTemplateMetadataException("template.json is not valid JSON.", exception);
        }
    }
}