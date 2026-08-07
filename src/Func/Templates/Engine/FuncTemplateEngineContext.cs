// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Captures resolved template context for one command invocation.
/// </summary>
/// <param name="Stack">Resolved project stack.</param>
/// <param name="Language">Resolved project language.</param>
/// <param name="BundleId">Resolved extension bundle identity.</param>
/// <param name="BundleVersion">Resolved extension bundle version.</param>
internal sealed record FuncTemplateEngineContext(string? Stack, string? Language, string? BundleId, string? BundleVersion);
