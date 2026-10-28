// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Describes a compatibility check for internal callers; codes are not a public command-output contract.
/// </summary>
internal sealed record ItemTemplateCompatibilityDiagnostic(string Code, string Message, string? CapabilityId = null);
