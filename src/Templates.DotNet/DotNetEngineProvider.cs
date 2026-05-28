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

    public DotNetEngineProvider(IInstalledTemplatesWorkloads installed, IDotnetTemplateRunner runner)
    {
        _installed = installed ?? throw new ArgumentNullException(nameof(installed));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public string EngineId => EngineIds.DotNet;

    public async Task<IReadOnlyList<FunctionTemplateInfo>> ListTemplatesAsync(
        TemplateListContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<InstalledTemplatesWorkload> rows = await _installed.ListInstalledAsync(context.Stack, cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        InstalledTemplatesWorkload selected = rows
            .OrderByDescending(r => r.PackageVersion, StringComparer.Ordinal)
            .First();

        DotNetTemplatesIndex? index = DotNetPayloadReader.Load(selected.InstallDirectory);
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

        try
        {
            DotnetTemplateRunResult result = await _runner.RunAsync(
                context.Template.Id,
                context.WorkingDirectory.Info,
                args,
                cancellationToken);

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
