// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.PostActions;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Restores generated .NET files after template creation.
/// </summary>
internal sealed class DotNetRestorePostActionProcessor(IDotnetCliRunner dotnetCli, DotNetProjectStack stack)
    : RestorePostAction
{
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));
    private readonly DotNetProjectStack _stack = stack ?? throw new ArgumentNullException(nameof(stack));

    /// <inheritdoc />
    public override bool CanProcess(PostActionSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return string.Equals(context.Stack, _stack.Stack, StringComparison.OrdinalIgnoreCase)
            && _stack.SupportedLanguageAliases.Any(language =>
                string.Equals(context.Language, language.Key, StringComparison.OrdinalIgnoreCase)
                || language.Value.Contains(context.Language, StringComparer.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    protected override async Task<bool> RestoreAsync(
        PostActionContext context, string targetPath, CancellationToken cancellationToken)
    {
        try
        {
            await _dotnetCli.RunStreamingAsync(
                ["restore", targetPath, "-tlp:verbosity=quiet", "--no-logo"],
                Path.GetFullPath(context.OutputBasePath),
                line => context.Reporter.WriteLog(line),
                line => context.Reporter.WriteLog(line, FunctionsReportSeverity.Error),
                cancellationToken);
        }
        catch (DotnetCliException ex)
        {
            context.Reporter.WriteLog(ex.Message, FunctionsReportSeverity.Error);
            return false;
        }
        catch (Win32Exception ex)
        {
            context.Reporter.WriteLog($"Could not start 'dotnet restore': {ex.Message}", FunctionsReportSeverity.Error);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            context.Reporter.WriteLog($"Could not start 'dotnet restore': {ex.Message}", FunctionsReportSeverity.Error);
            return false;
        }

        return true;
    }
}
