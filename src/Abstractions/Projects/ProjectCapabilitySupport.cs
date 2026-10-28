// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes a language stack's knowledge of one project capability, not a template's aggregate compatibility.
/// </summary>
public enum ProjectCapabilitySupport
{
    /// <summary>
    /// The language stack inspected the project but could not establish whether it supports the capability.
    /// This differs from an omitted capability, for which the stack supplies no report.
    /// </summary>
    Unknown,

    /// <summary>
    /// The language stack established from captured project inputs that the project supports the capability.
    /// </summary>
    Supported,

    /// <summary>
    /// The language stack established from captured project inputs that the project does not support the capability.
    /// Missing or inconclusive information must not be reported as Unsupported.
    /// </summary>
    Unsupported,
}
