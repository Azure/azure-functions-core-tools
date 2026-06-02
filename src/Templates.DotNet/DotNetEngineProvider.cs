// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// DotNet engine provider. Reads each installed
/// <c>Workloads.Templates.DotNet</c> row's <c>dotnet-templates.json</c>
/// catalog, projects records into <see cref="FunctionTemplateInfo"/>
/// (dedup'd by <c>groupIdentity</c>), and shells out to <c>dotnet new</c>
/// for the actual scaffold via <see cref="IDotnetTemplateRunner"/>.
/// </summary>
/// <remarks>
/// Read paths (catalog + per-template help) are offline-deterministic from
/// the workload payload — no <c>dotnet new</c> invocation, no NuGet I/O
/// (templates-workload-spec.md §5.3, §5.3.1). The hive is provisioned from
/// <c>source.json</c> at <c>func workload install</c> time so the apply
/// shell-out is offline too.
/// </remarks>
internal sealed class DotNetEngineProvider : ITemplateEngineProvider
{
    private readonly IInstalledTemplatesWorkloads _installed;
    private readonly IDotnetTemplateRunner _runner;
    private readonly IItemTemplateHiveProvisioner? _hiveProvisioner;

    public DotNetEngineProvider(IInstalledTemplatesWorkloads installed, IDotnetTemplateRunner runner)
        : this(installed, runner, hiveProvisioner: null)
    {
    }

    public DotNetEngineProvider(
        IInstalledTemplatesWorkloads installed,
        IDotnetTemplateRunner runner,
        IItemTemplateHiveProvisioner? hiveProvisioner)
    {
        _installed = installed ?? throw new ArgumentNullException(nameof(installed));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _hiveProvisioner = hiveProvisioner;
    }

    public string EngineId => EngineIds.DotNet;

    public async Task<IReadOnlyList<FunctionTemplateInfo>> ListTemplatesAsync(
        TemplateListContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? installDir = context.InstallDirectory;
        if (installDir is null)
        {
            // Fallback for callers that invoke the provider directly
            // (e.g. unit tests with no orchestrator). DotNet has no channel
            // axis (templates-workload-spec § 4.4.3) so highest-installed is
            // the right pick.
            IReadOnlyList<InstalledTemplatesWorkload> rows = await _installed.ListInstalledAsync(context.Stack, cancellationToken);
            if (rows.Count == 0)
            {
                return [];
            }

            installDir = rows
                .OrderByDescending(r => r.PackageVersion, StringComparer.Ordinal)
                .First()
                .InstallDirectory;
        }

        DotNetTemplatesIndex? index = DotNetPayloadReader.Load(installDir);
        if (index is null)
        {
            return [];
        }

        IReadOnlyList<FunctionTemplateInfo> projected = DotNetTemplateProjection.ProjectAll(index, context.Stack);
        if (string.IsNullOrWhiteSpace(context.Language))
        {
            return projected;
        }

        return [.. projected.Where(p => MatchesLanguage(p, context.Language))];
    }

    public async Task<TemplateApplicationResult> ApplyAsync(
        NewContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);

        // PR3: build a minimal arg list — `--name <FunctionName>` plus each
        // declared prompt's default (or the user-supplied value once PR4
        // wires the stage-B parse). The orchestrator owns stage-B and
        // supplies a richer arg list in PR4.
        var args = new List<string>
        {
            "--name",
            context.FunctionName,
        };

        if (!string.IsNullOrWhiteSpace(context.Language))
        {
            args.Add("--language");
            args.Add(MapToDotnetLanguageFlag(context.Language));
        }

        foreach (TemplateUserPrompt prompt in context.Template.Metadata.UserPrompts)
        {
            if (string.IsNullOrWhiteSpace(prompt.DefaultValue))
            {
                continue;
            }

            args.Add("--" + prompt.Id);
            args.Add(prompt.DefaultValue);
        }

        // Map --force through to `dotnet new`; without it the engine refuses
        // with exit 73 on any collision (e.g. readme.md from the timer template).
        if (context.Force)
        {
            args.Add("--force");
        }

        try
        {
            // Provision the custom item-template hive on demand from the
            // chosen workload's source.json (templates-workload-spec §6.3 /
                // func-new spec §4.8.3). Skip when no provisioner was injected
                // (test path) or when no install dir is known.
            string? customHivePath = null;
            if (_hiveProvisioner is not null && !string.IsNullOrWhiteSpace(context.InstallDirectory))
            {
                customHivePath = await _hiveProvisioner.EnsureProvisionedAsync(
                    context.InstallDirectory!,
                    cancellationToken);
            }

            DotnetTemplateRunResult result = await _runner.RunAsync(
                context.Template.Id,
                context.WorkingDirectory.Info,
                args,
                cancellationToken,
                customHivePath);

            if (result.ExitCode != 0)
            {
                string message = string.IsNullOrWhiteSpace(result.StandardError)
                    ? $"dotnet new '{context.Template.Id}' exited with code {result.ExitCode}."
                    : $"dotnet new '{context.Template.Id}' exited with code {result.ExitCode}: {result.StandardError.Trim()}";
                return new TemplateApplicationResult.Failed(
                    new TemplateApplicationFailure.ProviderError(message, null));
            }

            // dotnet new's stdout enumerates created files; in PR3 we
            // return the working directory as a single entry. PR4 can
            // parse the output more carefully.
            return new TemplateApplicationResult.Created([context.WorkingDirectory.Info.FullName]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return new TemplateApplicationResult.Failed(
                new TemplateApplicationFailure.ProviderError(ex.Message, ex));
        }
    }

    private static bool MatchesLanguage(FunctionTemplateInfo info, string requestedLanguage)
    {
        if (info.Languages.Count == 0)
        {
            return true;
        }

        // Accept the canonical id (csharp / fsharp) and the dotnet-templates
        // "language" field's typical capitalisation (C# / F#).
        return info.Languages.Any(l => string.Equals(l, requestedLanguage, StringComparison.OrdinalIgnoreCase))
            || info.Languages.Any(l => string.Equals(MapToCanonical(l), requestedLanguage, StringComparison.OrdinalIgnoreCase));
    }

    private static string MapToCanonical(string dotnetLanguage) =>
        dotnetLanguage.Replace("#", "sharp", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

    private static string MapToDotnetLanguageFlag(string canonical) =>
        canonical.ToLowerInvariant() switch
        {
            "csharp" => "C#",
            "fsharp" => "F#",
            _ => canonical,
        };
}
