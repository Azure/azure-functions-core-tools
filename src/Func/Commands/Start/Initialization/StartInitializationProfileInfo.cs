// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolved profile details surfaced by start initialization output.
/// </summary>
internal sealed record StartInitializationProfileInfo(
    string Name,
    string SourceKind,
    string SourceDisplayName,
    string HostVersionRange,
    IReadOnlyDictionary<string, string> WorkerVersionRanges,
    string? ExtensionBundleVersionRange,
    IReadOnlyList<string>? SupportedRuntimes,
    IReadOnlyList<StartInitializationProfileDiagnostic> Diagnostics);

/// <summary>
/// Profile diagnostic surfaced by start initialization output.
/// </summary>
internal sealed record StartInitializationProfileDiagnostic(string Severity, string Message);
