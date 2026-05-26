// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// A warning or note produced during profile resolution.
/// </summary>
internal sealed record ProfileDiagnostic(ProfileDiagnosticSeverity Severity, string Message);

/// <summary>
/// Profile diagnostic severity.
/// </summary>
internal enum ProfileDiagnosticSeverity
{
    Info,
    Warning,
}
