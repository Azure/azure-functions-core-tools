// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Provides function trigger templates for .NET projects. Uses the
/// Microsoft.Azure.Functions.Worker.ItemTemplates NuGet template pack
/// and delegates to 'dotnet new' for scaffolding.
/// </summary>
public class DotnetTemplateProvider : ITemplateProvider
{
    private readonly IDotnetCliRunner _dotnetCli;

    internal const string ItemTemplatePackageId = "Microsoft.Azure.Functions.Worker.ItemTemplates";

    private static readonly Dictionary<string, string> _templateShortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["McpToolTrigger"] = "mcptooltrigger",
        ["HttpTrigger"] = "http",
        ["TimerTrigger"] = "timer",
        ["QueueTrigger"] = "queue",
        ["BlobTrigger"] = "blob",
        ["CosmosDBTrigger"] = "cosmos",
        ["EventHubTrigger"] = "eventhub",
        ["ServiceBusTrigger"] = "servicebus",
        ["EventGridTrigger"] = "eventgrid",
        ["DurableFunctionsOrchestration"] = "durable",
    };

    private static readonly FunctionTemplate[] _templates =
    [
        new("HttpTrigger", "A function triggered by an HTTP request", "dotnet"),
        new("TimerTrigger", "A function triggered on a schedule (CRON expression)", "dotnet"),
        new("QueueTrigger", "A function triggered by an Azure Storage Queue message", "dotnet"),
        new("BlobTrigger", "A function triggered by an Azure Blob Storage event", "dotnet"),
        new("CosmosDBTrigger", "A function triggered by Azure Cosmos DB changes", "dotnet"),
        new("EventHubTrigger", "A function triggered by an Azure Event Hub event", "dotnet"),
        new("ServiceBusTrigger", "A function triggered by an Azure Service Bus message", "dotnet"),
        new("EventGridTrigger", "A function triggered by an Azure Event Grid event", "dotnet"),
        new("DurableFunctionsOrchestration", "A Durable Functions orchestration", "dotnet"),
        new("McpToolTrigger", "An MCP tool function callable by AI agents via the Model Context Protocol", "dotnet"),
        // TODO: Add McpResourceTrigger when available in the item template pack
    ];

    public DotnetTemplateProvider(IDotnetCliRunner dotnetCli)
    {
        _dotnetCli = dotnetCli;
    }

    public string WorkerRuntime => "dotnet";

    public Task<IReadOnlyList<FunctionTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<FunctionTemplate>>(_templates);
    }

    public async Task ScaffoldAsync(FunctionScaffoldContext context, CancellationToken cancellationToken = default)
    {
        if (!_templateShortNames.TryGetValue(context.TemplateName, out var shortName))
        {
            throw new GracefulException($"Unknown dotnet template: '{context.TemplateName}'.",
                $"Available templates: {string.Join(", ", _templateShortNames.Keys)}");
        }

        await EnsureItemTemplatePackInstalledAsync(cancellationToken);

        var projectNamespace = DetectNamespace(context.OutputPath);

        var args = $"new {shortName} --name \"{context.FunctionName}\" --output \"{context.OutputPath}\"";

        if (projectNamespace is not null)
        {
            args += $" --namespace \"{projectNamespace}\"";
        }

        if (context.Language is not null)
        {
            args += $" --language \"{context.Language}\"";
        }

        if (context.Force)
        {
            args += " --force";
        }

        var result = await _dotnetCli.RunAsync(args, cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            throw new GracefulException(
                $"Failed to create function '{context.FunctionName}' from template '{context.TemplateName}'.",
                result.StandardError.Trim());
        }
    }

    private async Task EnsureItemTemplatePackInstalledAsync(CancellationToken cancellationToken)
    {
        var bundledVersion = BundledTemplateVersions.ItemTemplatesVersion;
        var version = await NuGetTemplateResolver.ResolveVersionAsync(
            ItemTemplatePackageId, bundledVersion, cancellationToken);

        if (await TryInstallTemplatePackAsync(ItemTemplatePackageId, version, cancellationToken))
        {
            return;
        }

        // If the resolved version failed and it differs from bundled, try the bundled version
        if (!version.Equals(bundledVersion, StringComparison.OrdinalIgnoreCase)
            && await TryInstallTemplatePackAsync(ItemTemplatePackageId, bundledVersion, cancellationToken))
        {
            return;
        }

        // Last resort: try installing from the bundled .nupkg shipped with the workload
        if (await TryInstallFromBundledNupkgAsync(ItemTemplatePackageId, bundledVersion, cancellationToken))
        {
            return;
        }

        throw new GracefulException(
            $"Failed to install template pack '{ItemTemplatePackageId}'.",
            $"Tried version '{version}' from NuGet and bundled version '{bundledVersion}'. " +
            "Check your internet connection or reinstall the dotnet workload.");
    }

    /// <summary>
    /// Tries to install a template pack by version from NuGet.
    /// Returns true if installed or already installed.
    /// </summary>
    private async Task<bool> TryInstallTemplatePackAsync(
        string packageId, string version, CancellationToken cancellationToken)
    {
        var result = await _dotnetCli.RunAsync(
            $"new install {packageId}::{version}",
            cancellationToken: cancellationToken);

        if (result.IsSuccess)
        {
            return true;
        }

        // Already installed at this version — that's fine
        if (result.StandardError.Contains("is already installed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to install a template from the bundled .nupkg file shipped
    /// alongside the workload assembly (in the templates/ subdirectory).
    /// </summary>
    private async Task<bool> TryInstallFromBundledNupkgAsync(
        string packageId, string version, CancellationToken cancellationToken)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(DotnetTemplateProvider).Assembly.Location);
        if (assemblyDir is null) return false;

        var nupkgPath = Path.Combine(assemblyDir, "templates",
            $"{packageId.ToLowerInvariant()}.{version}.nupkg");

        if (!File.Exists(nupkgPath)) return false;

        var result = await _dotnetCli.RunAsync(
            $"new install \"{nupkgPath}\"",
            cancellationToken: cancellationToken);

        return result.IsSuccess
            || result.StandardError.Contains("is already installed", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectNamespace(string directory)
    {
        var projectFile = Directory.EnumerateFiles(directory, "*.csproj").FirstOrDefault()
            ?? Directory.EnumerateFiles(directory, "*.fsproj").FirstOrDefault();

        if (projectFile is not null)
        {
            return Path.GetFileNameWithoutExtension(projectFile);
        }

        return Path.GetFileName(directory);
    }
}
