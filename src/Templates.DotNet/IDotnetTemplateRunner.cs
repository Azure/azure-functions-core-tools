// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Engine-side abstraction over the <c>dotnet new</c> shell-out the DotNet
/// engine performs at apply time (templates-workload-spec.md §6.3 /
/// func-new spec §4.3). Kept narrow on purpose so tests can substitute the
/// process invocation without standing up a real <c>dotnet</c> child.
/// </summary>
internal interface IDotnetTemplateRunner
{
    /// <summary>
    /// Runs <c>dotnet new &lt;shortName&gt; --output &lt;workingDirectory&gt; …extraArgs</c>
    /// against the dotnet template hive that <c>func workload install</c>
    /// provisioned from <c>content/source.json</c>. Returns the process's
    /// exit code; non-zero is surfaced as
    /// <see cref="TemplateApplicationFailure.ProviderError"/> by the engine.
    /// </summary>
    /// <param name="shortName">
    /// The template <c>shortName</c> the upstream <c>dotnet new</c> CLI accepts
    /// — sourced from <see cref="DotNetTemplateRecord.ShortNames"/>[0]
    /// (= <see cref="FunctionTemplateInfo.Id"/> for DotNet templates).
    /// </param>
    /// <param name="workingDirectory">
    /// The project directory the scaffold writes into.
    /// </param>
    /// <param name="extraArgs">
    /// Additional CLI arguments (e.g. <c>--name MyFn --AccessRights Anonymous</c>)
    /// the orchestrator built from the stage-B parsed values. Already
    /// shell-safe — the implementation forwards them verbatim.
    /// </param>
    /// <param name="customHivePath">
    /// Optional CLI-scoped custom template hive (passed as
    /// <c>--debug:custom-hive &lt;path&gt;</c>). When non-null, <c>dotnet new</c>
    /// is isolated from the user's machine-global templating store and
    /// reads from the hive the engine provisioned from the templates
    /// workload's <c>source.json</c>. When null, <c>dotnet new</c> uses
    /// its default hive (caller has already ensured the templates are
    /// installed there).
    /// </param>
    public Task<DotnetTemplateRunResult> RunAsync(
        string shortName,
        DirectoryInfo workingDirectory,
        IReadOnlyList<string> extraArgs,
        CancellationToken cancellationToken,
        string? customHivePath = null);
}

/// <summary>
/// Outcome of <see cref="IDotnetTemplateRunner.RunAsync"/>. Combined stdout
/// + stderr text is captured for diagnostics; the runner does not stream
/// to the user's console (the engine decides how to surface output).
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="StandardOutput">Captured stdout.</param>
/// <param name="StandardError">Captured stderr.</param>
internal sealed record DotnetTemplateRunResult(int ExitCode, string StandardOutput, string StandardError);
