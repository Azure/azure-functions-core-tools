// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Allow-lists repository URLs for quickstart entries.
/// Only HTTPS GitHub URLs from trusted organizations are accepted.
/// </summary>
internal static class QuickstartUrlValidator
{
    private const string RequiredScheme = "https";
    private const string RequiredHost = "github.com";

    internal static readonly IReadOnlySet<string> TrustedOrganizations =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "azure",
            "azure-samples",
            "microsoft",
        };

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="url"/> is an HTTPS URL
    /// on <c>github.com</c> whose first path segment is one of the
    /// <see cref="TrustedOrganizations"/>, with no embedded credentials.
    /// </summary>
    internal static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, RequiredScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (!string.Equals(uri.Host, RequiredHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!uri.IsDefaultPort)
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/', 3);
        return segments.Length >= 2
            && TrustedOrganizations.Contains(segments[0])
            && segments[1].Length > 0;
    }
}
