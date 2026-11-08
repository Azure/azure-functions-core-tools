// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Identifies one workload-selected project template and its ordinary input values.
/// </summary>
public sealed record ProjectTemplateRequest
{
    public ProjectTemplateRequest(string templateIdentity, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateIdentity);
        ArgumentNullException.ThrowIfNull(parameters);

        TemplateIdentity = templateIdentity;
        Parameters = parameters.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public string TemplateIdentity { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }
}
