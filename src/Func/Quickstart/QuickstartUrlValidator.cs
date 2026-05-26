// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Allow-lists repository URLs for quickstart entries.
/// </summary>
internal static class QuickstartUrlValidator
{
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
    /// <see cref="TrustedOrganizations"/>.
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

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Reject URLs with embedded credentials (e.g. https://user:pass@github.com/…).
        // Public Azure-Samples / Azure / Microsoft repos never need them, so a
        // non-empty UserInfo is always a misconfigured or malicious entry.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/', 2);
        return segments.Length >= 1 && TrustedOrganizations.Contains(segments[0]);
    }
}
