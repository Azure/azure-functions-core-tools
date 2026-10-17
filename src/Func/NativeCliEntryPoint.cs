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
/// Runs the explicitly selected staged full CLI entry; Program keeps the legacy policy until a separate rollout.
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
        try
        {
            using NativeTemplateWorkflowHost host = await new NativeTemplateWorkflowHostFactory().CreateAsync(
                interaction, workloads, configuration, services =>
                {
                    services.AddFirstPartyNativeWorkflows();
                    configure?.Invoke(services);
                }, enableBundleBindingConstraints: true, cancellationToken);
            await host.StartAsync(cancellationToken);
            using NativeTemplateWorkflowInvocation invocation = host.CreateInvocation();
            var binding = new NativeEntryCommandBinding(invocation.Services, invocation.Composer, original);
            FuncRootCommand root = Parser.CreateCommand(invocation.Services, binding);
            ParseResult parsed = root.Parse(LegacyStartArgumentNormalizer.Normalize(original), new ParserConfiguration
            {
                EnablePosixBundling = false,
                ResponseFileTokenReplacer = null,
            });
            commandName = CommandNameResolver.ResolveCommandName(parsed, root);
            activity?.SetCommandName(commandName);
            bool json = parsed.CommandResult.Command is NewCommand item && NativeEntryCommandBinding.IsJsonOutput(item, parsed);
            bool nonInteractive = parsed.CommandResult.Command is NewCommand command
                && NativeEntryCommandBinding.IsNonInteractive(command, parsed);
            bool advisories = !nonInteractive && !SetupCommand.ShouldSuppressAdvisories(parsed);
            Task<string?> notice = advisories
                ? (versionCheck ?? VersionChecker.CheckForUpdateAsync)(cancellationToken) : Task.FromResult<string?>(null);
            if (json && parsed.Errors.Count > 0)
            {
                interaction.WriteJson(new { kind = "invalidArguments", succeeded = false, diagnostics = parsed.Errors.Select(error => error.Message) });
                return exit;
            }

            IFirstRunCoordinator firstRun = invocation.Services.GetRequiredService<IFirstRunCoordinator>();
            bool stagedCreation = parsed.CommandResult.Command is InitCommand or NewCommand;
            int? firstRunResult = json || nonInteractive || stagedCreation ? null
                : await firstRun.EnsureFirstRunPromptedAsync(commandName, parsed, cancellationToken);
            exit = firstRunResult ?? await parsed.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellationToken);
            if (advisories && !cancellationToken.IsCancellationRequested)
            {
                Task finished = await Task.WhenAny(notice, Task.Delay(1000, cancellationToken));
                if (ReferenceEquals(finished, notice) && await notice is { } latest)
                {
                    interaction.WriteBlankLine();
                    interaction.WriteLine(line => line.Warning($"A newer version of Azure Functions CLI is available ({latest})."));
                }

                invocation.Services.GetRequiredService<FuncAliasNudge>().TryPrint(exit, commandName);
            }

            return exit;
        }
        catch (OperationCanceledException)
        {
            exit = 130;
            return exit;
        }
        catch (GracefulException exception)
        {
            activity?.Fail(exception);
            interaction.WriteError(exception.Message);
            return exit;
        }
        catch (Exception exception)
        {
            activity?.Fail(exception);
            interaction.WriteError($"An unexpected error occurred: {exception.Message}");
            return exit;
        }
        finally
        {
            stopwatch.Stop();
            CliTelemetry.Metric.RecordCommand(commandName, exit, stopwatch.ElapsedMilliseconds);
        }
    }
}
