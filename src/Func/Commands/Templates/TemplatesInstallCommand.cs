// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Packages;

namespace Azure.Functions.Cli.Commands.Templates;

/// <summary>
/// Installs one validated remote template package into the func hive.
/// </summary>
internal sealed class TemplatesInstallCommand : FuncCliCommand
{
    private readonly TemplatePackageService _templatePackageService;
    private readonly IInteractionService _interaction;

    public Argument<string> PackageArgument { get; } = new("package-id") { Description = "Remote NuGet package ID to install." };

    public Option<string?> VersionOption { get; } = new("--version") { Description = "Exact SemVer version, including prerelease versions." };

    public Option<string?> SourceOption { get; } = new("--source")
    {
        Description = "Single HTTP(S) V3 NuGet feed. Default: FUNC_CLI_TEMPLATES_SOURCE, then nuget.org. NuGet.Config is not read.",
    };

    public Option<bool> PrereleaseOption { get; } = new("--prerelease") { Description = "Resolve the latest version including prereleases." };

    public TemplatesInstallCommand(TemplatePackageService templatePackageService, IInteractionService interaction)
        : base("install", "Install a FuncTemplate package (latest stable by default).")
    {
        _templatePackageService = templatePackageService ?? throw new ArgumentNullException(nameof(templatePackageService));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        Arguments.Add(PackageArgument);
        Options.Add(VersionOption);
        Options.Add(SourceOption);
        Options.Add(PrereleaseOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        bool changed;
        try
        {
            changed = await _templatePackageService.InstallAsync(parseResult.GetValue(PackageArgument)!,
                parseResult.GetValue(VersionOption), parseResult.GetValue(SourceOption), parseResult.GetValue(PrereleaseOption), cancellationToken);
        }
        catch (TemplatePackageException ex)
        {
            throw new GracefulException(ex.Message, ex, isUserError: true);
        }
        catch (ArgumentException ex)
        {
            throw new GracefulException(ex.Message, ex, isUserError: true);
        }
        catch (IOException ex)
        {
            throw new GracefulException($"Cannot access the template archive or hive. Check filesystem access and retry. {ex.Message}", ex, isUserError: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new GracefulException($"Template hive access was denied. Check permissions and retry. {ex.Message}", ex, isUserError: true);
        }

        _interaction.WriteSuccess(changed ? "Template package installed." : "The same template package version is already installed.");
        return 0;
    }
}
