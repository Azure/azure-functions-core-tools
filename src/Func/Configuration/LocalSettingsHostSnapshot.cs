// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Configuration;

internal sealed class LocalSettingsHostSnapshot
{
    public int? LocalHttpPort { get; init; }

    public string? Cors { get; init; }

    public bool? CorsCredentials { get; init; }
}
