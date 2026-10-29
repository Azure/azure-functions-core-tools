// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Templates.Preparation;

/// <summary>
/// Retains already serialized CLI configuration for one project root relative to the operation's target directory.
/// </summary>
internal sealed class PreparedConfiguration
{
    public PreparedConfiguration(string projectRootRelativePath, ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootRelativePath);
        if (content.IsEmpty)
        {
            throw new ArgumentException("Prepared configuration requires nonempty serialized bytes.", nameof(content));
        }

        ProjectRootRelativePath = projectRootRelativePath == "." ? "." : PreparedOutputEntry.NormalizeRelativePath(projectRootRelativePath);
        string prefix = ProjectRootRelativePath == "." ? string.Empty : ProjectRootRelativePath + "/";
        Entry = new(prefix + CliConfigurationNames.ProjectConfigFolderName + "/" + CliConfigurationNames.ConfigFileName, PreparedOutputKind.File, content);
    }

    public string ProjectRootRelativePath { get; }

    public PreparedOutputEntry Entry { get; }
}
