// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class NativeTemplatePreparationSnapshot
{
    private readonly IReadOnlyDictionary<string, byte[]> _files;

    public NativeTemplatePreparationSnapshot(TemplateEffectSource source, ICreationEffects effects,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> expandedFiles, ICreationResult? creation = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(expandedFiles);
        Effects = new CapturedEffects(effects, creation ?? effects.CreationResult);
        _files = new ReadOnlyDictionary<string, byte[]>(expandedFiles.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
    }

    public TemplateEffectSource Source { get; }

    public ICreationEffects Effects { get; }

    public IReadOnlyList<TemplateEffectSource> Sources { get; init; } = [];

    public IReadOnlyList<NativeTemplateOutputSource> OutputSources { get; init; } = [];

    public IReadOnlyList<string> Directories { get; init; } = [];

    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> ExpandedFiles => new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(
        _files.ToDictionary(pair => pair.Key, pair => new ReadOnlyMemory<byte>([.. pair.Value]), StringComparer.Ordinal));

    private sealed class CapturedEffects(ICreationEffects original, ICreationResult creation) : ICreationEffects
    {
        public IReadOnlyList<IFileChange> FileChanges { get; } = Array.AsReadOnly<IFileChange>(
            [.. original.FileChanges.Select(change => change is IFileChange2 mapping
                ? (IFileChange)new MappedChange(change, mapping.SourceRelativePath) : new CapturedChange(change))]);

        public ICreationResult CreationResult { get; } = new CapturedCreation(creation);
    }

    private class CapturedChange(IFileChange original) : IFileChange
    {
        public string TargetRelativePath { get; } = original.TargetRelativePath;

        public ChangeKind ChangeKind { get; } = original.ChangeKind;

        public byte[] Contents => [];
    }

    private sealed class MappedChange(IFileChange original, string source) : CapturedChange(original), IFileChange2
    {
        public string SourceRelativePath { get; } = source;
    }

    private sealed class CapturedCreation(ICreationResult original) : ICreationResult
    {
        public IReadOnlyList<IPostAction> PostActions { get; } = Array.AsReadOnly<IPostAction>([.. original.PostActions.Select(action => new CapturedAction(action))]);

        public IReadOnlyList<ICreationPath> PrimaryOutputs { get; } = Array.AsReadOnly<ICreationPath>([.. original.PrimaryOutputs.Select(path => new CapturedPath(path.Path))]);
    }

    private sealed record CapturedPath(string Path) : ICreationPath;

    private sealed class CapturedAction(IPostAction original) : IPostAction
    {
        public string? Description { get; } = original.Description;

        public Guid ActionId { get; } = original.ActionId;

        public bool ContinueOnError { get; } = original.ContinueOnError;

        public IReadOnlyDictionary<string, string> Args { get; } = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(original.Args, StringComparer.Ordinal));

        public string? ManualInstructions { get; } = original.ManualInstructions;
    }
}

internal sealed record NativeTemplatePreparationResult(NativeTemplatePreparationSnapshot? Snapshot, TemplateInvocationResult? Failure);
