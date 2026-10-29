// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.PackageIntake;

namespace Azure.Functions.Cli.Templates.Integration;

internal sealed class PublicTemplatePackageOptions
{
    public PublicTemplatePackageBinding? Qualification { get; init; }
}

/// <summary>
/// Binds independently selected package bytes to one project default and one public item alias.
/// </summary>
internal sealed class PublicTemplatePackageBinding(
    TemplatePackageIdentity identity, string resourceName, string projectIdentity, string itemIdentity, string publicItemAlias)
{
    public TemplatePackageIdentity Identity { get; } = ValidateIdentity(identity);

    public string ResourceName { get; } = Text(resourceName, nameof(resourceName));

    public string ProjectIdentity { get; } = Text(projectIdentity, nameof(projectIdentity));

    public string ItemIdentity { get; } = Text(itemIdentity, nameof(itemIdentity));

    public string PublicItemAlias { get; } = Text(publicItemAlias, nameof(publicItemAlias));

    private static TemplatePackageIdentity ValidateIdentity(TemplatePackageIdentity value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(identity));
        if (value.ExpectedSha256 is null)
        {
            throw new ArgumentException("An internally bound package requires an independently selected SHA-256.", nameof(identity));
        }

        return value;
    }

    private static string Text(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }
}
