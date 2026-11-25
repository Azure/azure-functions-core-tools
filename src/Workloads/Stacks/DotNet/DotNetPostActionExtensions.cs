// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.PostActions;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Shared helpers for .NET template post actions.
/// </summary>
internal static class DotNetPostActionExtensions
{
    extension(IDotnetCliRunner dotnetCli)
    {
        /// <summary>
        /// Runs <c>dotnet</c> for a post action, streaming output to the context's reporter.
        /// </summary>
        /// <param name="context">The post-action context.</param>
        /// <param name="arguments">The arguments to pass to <c>dotnet</c>.</param>
        /// <param name="commandName">The command name used in start-failure messages.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>True when the command succeeds; false when it could not start or exited with an error.</returns>
        public async Task<bool> TryRunPostActionAsync(
            PostActionContext context,
            IReadOnlyList<string> arguments,
            string commandName,
            CancellationToken cancellationToken)
        {
            try
            {
                await dotnetCli.RunStreamingAsync(
                    arguments,
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
            catch (GracefulException ex)
            {
                context.Reporter.WriteLog(ex.Message, FunctionsReportSeverity.Error);
                return false;
            }
            catch (Win32Exception ex)
            {
                context.Reporter.WriteLog(
                    $"Could not start '{commandName}': {ex.Message}", FunctionsReportSeverity.Error);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                context.Reporter.WriteLog(
                    $"Could not start '{commandName}': {ex.Message}", FunctionsReportSeverity.Error);
                return false;
            }

            return true;
        }
    }
}
