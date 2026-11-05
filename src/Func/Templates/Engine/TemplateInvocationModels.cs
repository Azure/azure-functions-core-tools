// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal enum TemplateInvocationMode
{
    Create,
    DryRun,
}

internal sealed record TemplateInvocationRequest(
    string OutputPath,
    IReadOnlyDictionary<string, string> Parameters,
    bool Force,
    TemplateInvocationMode Mode);

internal sealed record TemplateInvocationResult(
    IReadOnlyList<TemplateFileChange> FileChanges,
    bool WasCreated);

internal sealed record TemplateFileChange(string RelativePath, TemplateFileChangeKind Kind);

internal enum TemplateFileChangeKind
{
    Create,
    Modify,
    Delete,
}
