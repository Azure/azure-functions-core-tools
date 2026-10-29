// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Templates.Integration;

internal enum TemplatePreparationFailure
{
    SelectionRejected,
    MissingRequiredInput,
    NativeIneligible,
    ProjectNotResolved,
    ProjectMismatch,
    ProjectIncompatible,
    RenderFailed,
    InvalidNativeInput,
    CaptureRejected,
    InternalTemplate,
}

/// <summary>
/// Preserves a preparation rejection and its selection or compatibility diagnostics for command-owned presentation.
/// </summary>
internal sealed class TemplatePreparationException(
    TemplatePreparationFailure failure,
    string message,
    TemplateSelectionResult? selection = null,
    ItemTemplateCompatibilityResult? compatibility = null,
    IEnumerable<string>? diagnostics = null,
    ProjectCreationFailure? projectFailure = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public TemplatePreparationFailure Failure { get; } = Enum.IsDefined(failure)
        ? failure
        : throw new ArgumentOutOfRangeException(nameof(failure));

    public TemplateSelectionResult? Selection { get; } = selection;

    public ItemTemplateCompatibilityResult? Compatibility { get; } = compatibility;

    public ImmutableArray<string> Diagnostics { get; } = [.. diagnostics ?? []];

    public ProjectCreationFailure? ProjectFailure { get; } = projectFailure;
}
