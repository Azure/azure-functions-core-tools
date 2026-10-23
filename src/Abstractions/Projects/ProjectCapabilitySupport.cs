// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes a runtime owner's knowledge of one semantic project capability.
/// </summary>
public enum ProjectCapabilitySupport
{
    Unknown,
    Supported,
    Unsupported,
}
