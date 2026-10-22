// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Projects;
using Azure.Functions.Cli.Templates.Go.Shared;

namespace Azure.Functions.Cli.Templates.Go;

/// <summary>
/// Runs the shared Go init helper only after a source-qualified native project and canonical configuration exist.
/// </summary>
internal sealed class GoNativeProjectPreparation(IFileSystem fileSystem, GoProjectPreparation preparation) : INativeProjectPreparation
{
    internal static readonly OwnedProjectPreparationDefinition Definition = new(
        "go", "Go", "Azure.Functions.Templates.Go.Basic", "Azure.Functions.Go.Basic", "provenance/go/project.json",
        ["content/project/.template.config/template.json", "content/project/.template.config/func.host.json",
            "content/project/host.json", "content/project/go.mod", "content/project/main.go", "content/project/local.settings.json",
            "content/project/.funcignore", "content/project/.gitignore"]);
    private readonly OwnedProjectPreparationSource _sources = new(fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)));
    private readonly IFileSystem _files = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly GoProjectPreparation _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
    private readonly ConditionalWeakTable<NativeProjectPreparationPreview, BoundPreview> _previews = [];

    public async Task<NativeProjectPreparationPreview> PreviewAsync(NativeProjectPreparationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            QualifiedProjectPreparationSource source = await _sources.QualifyAsync(context, Definition, cancellationToken);
            if (!context.Parameters.TryGetValue("SkipGoModTidy", out string? value) || !bool.TryParse(value, out bool skip))
            {
                return Invalid("Go preparation requires the declared canonical SkipGoModTidy value.");
            }
            NativeProjectPreparationPreview preview = new(true, [new(NativeProjectPreparationOperation.GoModTidy, skip)], []);
            _previews.Add(preview, new(context, source.ManifestSha256, skip, new Dictionary<string, string?>(context.Parameters)));
            return preview;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Invalid($"Cannot qualify Go preparation: {exception.Message}");
        }
    }

    public async Task<NativeProjectPreparationResult> CompleteAsync(NativeProjectPreparationContext context,
        NativeProjectPreparationPreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!_previews.TryGetValue(preview, out BoundPreview? bound) || !preview.Succeeded
                || !ReferenceEquals(context.SelectedTemplate, bound.Context.SelectedTemplate)
                || context.TargetDirectory.Info.FullName != bound.Context.TargetDirectory.Info.FullName
                || context.Parameters.Count != bound.Parameters.Count
                || context.Parameters.Any(pair => !bound.Parameters.TryGetValue(pair.Key, out string? original) || original != pair.Value)
                || !context.Parameters.TryGetValue("SkipGoModTidy", out string? value)
                || !bool.TryParse(value, out bool skip) || skip != bound.Skip)
            {
                return new(false, ["Go preparation does not match its source-qualified preview."]);
            }
            QualifiedProjectPreparationSource current = await _sources.QualifyAsync(context, Definition, cancellationToken);
            if (current.ManifestSha256 != bound.ManifestSha256)
            {
                return new(false, ["Go preparation provenance changed after preview; generated files were retained."]);
            }
            string? diagnostic = await _sources.ValidateCreatedAsync(context, Definition,
                ["host.json", "go.mod", "main.go", "local.settings.json"], cancellationToken);
            if (diagnostic is not null) return new(false, [diagnostic]);
            string? settings = await _files.ReadAllTextIfExistsAsync(
                Path.Combine(context.TargetDirectory.Info.FullName, "local.settings.json"), cancellationToken);
            if (settings is null) return new(false, ["Generated Go settings disappeared before preparation."]);
            using var local = JsonDocument.Parse(settings);
            if (OwnedProjectPreparationSource.Text(OwnedProjectPreparationSource.Property(local.RootElement, "Values"),
                "FUNCTIONS_WORKER_RUNTIME") != "native")
            {
                return new(false, ["Generated Go settings do not match the qualified native worker runtime."]);
            }
            GoProjectPreparationResult result = await _preparation.PrepareAsync(context.TargetDirectory.Info.FullName, bound.Skip, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result.Skipped ? new(true, ["Skipped 'go mod tidy'."])
                : result.ExitCode == 0 ? new(true, string.IsNullOrWhiteSpace(result.StandardError) ? [] : [result.StandardError])
                : new(false, [$"'go mod tidy' failed (exit {result.ExitCode}). Generated project files and CLI configuration were retained.",
                    result.StandardError]);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(false, [$"Go preparation could not complete; generated files were retained. {exception.Message}"]);
        }
    }

    private static NativeProjectPreparationPreview Invalid(string diagnostic) => new(false, [], [diagnostic]);

    private sealed record BoundPreview(NativeProjectPreparationContext Context, string ManifestSha256, bool Skip,
        IReadOnlyDictionary<string, string?> Parameters);
}
