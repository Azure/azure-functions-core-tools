// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Shared constants for quickstart template operations.
/// </summary>
public static class QuickstartConstants
{
    /// <summary>
    /// The required URL scheme for template repository URLs.
    /// </summary>
    public const string RequiredScheme = "https";

    /// <summary>
    /// The GitHub hostname used for URL validation and archive downloads.
    /// </summary>
    public const string GitHubHostName = "github.com";

    /// <summary>
    /// The git ref prefix required for all template entries.
    /// </summary>
    public const string TagRefPrefix = "refs/tags/";

    /// <summary>
    /// Filename of the Functions host configuration file.
    /// </summary>
    public const string HostJsonFileName = "host.json";
}
