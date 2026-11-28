// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Packages;

namespace Azure.Functions.Cli.Commands.Templates;

/// <summary>
/// Uninstalls a managed NuGet identity, not a template name.
/// </summary>
internal sealed class TemplatesUninstallCommand : FuncCliCommand
{
    private readonly TemplatePackageService _templatePackageService;
    private readonly IInteractionService _interaction;

    public Argument<string> PackageArgument { get; } = new("package-id") { Description = "Installed NuGet package ID to remove." };

    public TemplatesUninstallCommand(TemplatePackageService templatePackageService, IInteractionService interaction)
        : base("uninstall", "Uninstall a managed template package.")
    {
        _templatePackageService = templatePackageService ?? throw new ArgumentNullException(nameof(templatePackageService));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        Arguments.Add(PackageArgument);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        try
        {
            await _templatePackageService.UninstallAsync(parseResult.GetValue(PackageArgument)!, cancellationToken);
        }
        catch (TemplatePackageException ex)
        {
            throw new GracefulException(ex.Message, ex, isUserError: true);
        }
        catch (IOException ex)
        {
            throw new GracefulException($"Cannot access the template hive. Check filesystem access and retry. {ex.Message}", ex, isUserError: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new GracefulException($"Template hive access was denied. Check permissions and retry. {ex.Message}", ex, isUserError: true);
        }

        _interaction.WriteSuccess("Template package uninstalled.");
        return 0;
    }
}
