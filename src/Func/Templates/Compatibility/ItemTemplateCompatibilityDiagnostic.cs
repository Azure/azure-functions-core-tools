// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Compatibility;

internal sealed record ItemTemplateCompatibilityDiagnostic(string Code, string Message, string? CapabilityId = null);
