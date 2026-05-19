// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Configuration;

internal sealed class HostStartupOptions
{
    public const string SectionName = "HostStartup";

    public int? Port { get; set; }

    public string? Cors { get; set; }

    public bool? CorsCredentials { get; set; }
}
