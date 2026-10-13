// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed record TemplateInvocationRequest(WorkingDirectory OutputDirectory, IReadOnlyDictionary<string, string?> Parameters, string? Name = null, bool DryRun = false, bool Force = false);

internal enum TemplateInvocationStatus
{
    Success,
    Failed,
}

internal sealed record TemplateFileChange(string Path, string Kind);

internal sealed record TemplateInvocationResult(TemplateInvocationStatus Status, IReadOnlyList<TemplateFileChange> Changes, string? Diagnostic = null);

internal sealed record TemplateConfigurationSnapshot(string Content, string Fingerprint);