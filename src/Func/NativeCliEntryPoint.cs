// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Commands.Start;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Telemetry;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli;

/// <summary>
/// Runs public CLI commands with native project and item template workflows.
/// </summary>
internal sealed class NativeCliEntryPoint
{
    public async Task<int> ExecuteAsync(string[] arguments, IInteractionService interaction, WorkloadPathsOptions workloads,
        CliConfigurationPathsOptions configuration, CancellationToken cancellationToken,
        Action<IServiceCollection>? configure = null, Func<CancellationToken, Task<string?>>? versionCheck = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(workloads);
        ArgumentNullException.ThrowIfNull(configuration);
        string[] original = [.. arguments];
        using Activity? activity = CliTelemetry.Trace.StartCommandActivity();
        var stopwatch = Stopwatch.StartNew();
        string commandName = "unknown";
        int exit = 1;
        FuncRootCommand? root = null;
        ParseResult? parsed = null;
        bool advisories = false;
        Task<string?> notice = Task.FromResult<string?>(null);
        FuncAliasNudge? aliasNudge = null;
        try
        {
            using NativeTemplateWorkflowHost host = await new NativeTemplateWorkflowHostFactory().CreateAsync(
                interaction, workloads, configuration, services =>
                {
                    services.AddFirstPartyNativeWorkflows();
                    configure?.Invoke(services);
                }, enableBundleBindingConstraints: true, cancellationToken,
                deferProjectConfigurationErrors: true, arguments: original);
            if (!host.HasProjectConfigurationFailure)
            {
                await host.StartAsync(cancellationToken);
            }
            using NativeTemplateWorkflowInvocation invocation = host.CreateCommandScope();
            var binding = new NativeEntryCommandBinding(invocation.Services, original);
            root = Parser.CreateCommand(invocation.Services, binding);
            parsed = ParseArguments(root, original);
            if (!IsTemplateInvocation(parsed))
            {
                host.ThrowIfProjectConfigurationFailed();
            }
            if (host.HasProjectConfigurationFailure)
            {
                await host.StartAsync(cancellationToken);
            }
            commandName = CommandNameResolver.ResolveCommandName(parsed, root);
            activity?.SetCommandName(commandName);
            bool json = parsed.CommandResult.Command switch
            {
                NewCommand item => NativeEntryCommandBinding.IsJsonOutput(item, parsed),
                InitCommand project => NativeEntryCommandBinding.IsInitJsonOutput(project, parsed),
                _ => false,
            };
            bool nonInteractive = parsed.CommandResult.Command switch
            {
                NewCommand item => NativeEntryCommandBinding.IsNonInteractive(item, parsed),
                InitCommand project => NativeEntryCommandBinding.IsInitNonInteractive(project, parsed),
                _ => false,
            };
            advisories = !nonInteractive && !SetupCommand.ShouldSuppressAdvisories(parsed);
            notice = advisories
                ? (versionCheck ?? VersionChecker.CheckForUpdateAsync)(cancellationToken) : Task.FromResult<string?>(null);
            aliasNudge = advisories ? invocation.Services.GetRequiredService<FuncAliasNudge>() : null;
            if (json && parsed.Errors.Count > 0)
            {
                interaction.WriteJson(new { kind = "invalidArguments", succeeded = false, diagnostics = parsed.Errors.Select(error => error.Message) });
                return exit;
            }

            IFirstRunCoordinator firstRun = invocation.Services.GetRequiredService<IFirstRunCoordinator>();
            bool creation = parsed.CommandResult.Command is InitCommand or NewCommand;
            int? firstRunResult = json || nonInteractive || creation ? null
                : await firstRun.EnsureFirstRunPromptedAsync(commandName, parsed, cancellationToken);
            exit = firstRunResult ?? await parsed.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellationToken);
            return exit;
        }
        catch (OperationCanceledException)
        {
            exit = 130;
            return exit;
        }
        catch (GracefulException exception)
        {
            exit = 1;
            activity?.Fail(exception);
            interaction.WriteError(exception.Message);
            if (exception.VerboseMessage is not null && root is not null && parsed?.GetValue(root.VerboseOption) is true)
            {
                interaction.WriteHint(exception.VerboseMessage);
            }
            return exit;
        }
        catch (Exception exception)
        {
            exit = 1;
            activity?.Fail(exception);
            interaction.WriteError($"An unexpected error occurred: {exception.Message}");
            return exit;
        }
        finally
        {
            stopwatch.Stop();
            CliTelemetry.Metric.RecordCommand(commandName, exit, stopwatch.ElapsedMilliseconds);
            if (advisories && exit != 130 && !cancellationToken.IsCancellationRequested)
            {
                await PrintVersionNoticeAsync(interaction, notice, cancellationToken);
                aliasNudge?.TryPrint(exit, commandName);
            }
        }
    }

    private static async Task PrintVersionNoticeAsync(IInteractionService interaction, Task<string?> notice, CancellationToken cancellationToken)
    {
        try
        {
            Task finished = await Task.WhenAny(notice, Task.Delay(1000, cancellationToken));
            if (ReferenceEquals(finished, notice) && await notice is { } latest)
            {
                interaction.WriteBlankLine();
                interaction.WriteLine(line => line.Warning($"A newer version of Azure Functions CLI is available ({latest})."));
            }
        }
        catch (Exception)
        {
            // Version advisories are best-effort and must not change a completed command's exit code.
        }
    }

    internal static ParseResult ParseArguments(FuncRootCommand root, string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(arguments);
        string[] normalized = LegacyStartArgumentNormalizer.Normalize(arguments);
        ParseResult parsed = root.Parse(normalized, new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null,
        });
        return IsTemplateInvocation(parsed) ? parsed : root.Parse(normalized, new ParserConfiguration { EnablePosixBundling = false });
    }

    internal static bool IsTemplateInvocation(ParseResult parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        if (parsed.CommandResult.Command is InitCommand or NewCommand) return true;
        if (parsed.CommandResult.Command is not HelpCommand help) return false;
        string? requested = parsed.GetResult(help.CommandArgument)?.GetValueOrDefault<string?>();
        return parsed.RootCommandResult.Command.Subcommands.FirstOrDefault(command =>
            string.Equals(command.Name, requested, StringComparison.OrdinalIgnoreCase)) is InitCommand or NewCommand;
    }
}
