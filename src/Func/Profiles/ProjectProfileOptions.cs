// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Project-owned profile selection settings.
/// </summary>
internal sealed class ProjectProfileOptions
{
    [ConfigurationKeyName("$schema")]
    public string? Schema { get; set; }

    public List<string> Profiles { get; set; } = [];

    public string? DefaultProfile { get; set; }
}
