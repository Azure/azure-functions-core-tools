// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.StacksApi;

namespace Azure.Functions.Cli.Commands.Init;

public sealed class DotNetProjectInitializer : IProjectInitializer
{
    public DotNetProjectInitializer(bool inProc = false)
    {
        if (inProc)
        {
            Runtime = WorkerRuntime.Dotnet;
        }
    }

    public WorkerRuntime Runtime { get; set; } = WorkerRuntime.DotnetIsolated;

    public string ResolvedTargetFramework { get; set; }

    public async Task InitializeAsync(ParseResult args, CancellationToken ct)
    {
        var csx = args.GetValue(InitCommandParser.CsxOption);
        var force = args.GetValue(InitCommandParser.ForceOption);
        var requestedTfm = args.GetValue(InitCommandParser.TargetFrameworkOption);

        ResolvedTargetFramework = await ResolveTfmAsync(Runtime, requestedTfm, csx);

        // EOL warnings (kept from your existing logic)
        await ShowEolMessageIfNeeded(Runtime, ResolvedTargetFramework);

        var appName = Utilities.SanitizeLiteral(Path.GetFileName(Environment.CurrentDirectory), allowed: "-");
        await DotnetHelpers.DeployDotnetProject(appName, force, Runtime, ResolvedTargetFramework);
    }

    public async Task WriteDockerfileAsync(ParseResult args, CancellationToken ct)
    {
        var csx = args.GetValue(InitCommandParser.CsxOption);

        if (string.IsNullOrEmpty(ResolvedTargetFramework))
        {
            var requestedTfm = args.GetValue(InitCommandParser.TargetFrameworkOption);
            ResolvedTargetFramework = await ResolveTfmAsync(Runtime, requestedTfm, csx);
        }

        if (WorkerRuntimeLanguageHelper.IsDotnet(Runtime) && string.IsNullOrEmpty(ResolvedTargetFramework) && !csx)
        {
            var root = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
            if (root != null)
            {
                ResolvedTargetFramework = await DotnetHelpers.DetermineTargetFramework(root);
            }
        }

        if (Runtime == WorkerRuntime.Dotnet)
        {
            await InitDockerfileInProc(csx, ResolvedTargetFramework);
        }
        else
        {
            await InitDockerfile(ResolvedTargetFramework);
        }

        await FileSystemHelpers.WriteFileIfNotExists(".dockerignore", await StaticResources.DockerIgnoreFile);
    }

    public Task PostInstallAsync(ParseResult args, CancellationToken ct) => Task.CompletedTask;

    private static async Task<string> ResolveTfmAsync(WorkerRuntime runtime, string requested, bool csx)
    {
        if (!string.IsNullOrEmpty(requested) || csx)
        {
            return requested ?? TargetFramework.Net8;
        }

        try
        {
            // If not provided, try to infer from existing project root
            if (WorkerRuntimeLanguageHelper.IsDotnet(runtime))
            {
                var root = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
                if (root != null)
                {
                    return await DotnetHelpers.DetermineTargetFramework(root);
                }
            }
        }
        catch (CliException)
        {
            // GetFunctionAppRootDirectory may throw if no host.json is found so just default if there are any issues
            return runtime == WorkerRuntime.DotnetIsolated ? TargetFramework.Net8 : TargetFramework.Net8;
        }

        return runtime == WorkerRuntime.DotnetIsolated ? TargetFramework.Net8 : TargetFramework.Net8;
    }

    private async Task InitDockerfile(string tfm)
    {
        var content = tfm switch
        {
            TargetFramework.Net7 => await StaticResources.DockerfileDotnet7Isolated,
            TargetFramework.Net8 => await StaticResources.DockerfileDotnet8Isolated,
            TargetFramework.Net9 => await StaticResources.DockerfileDotnet9Isolated,
            TargetFramework.Net10 => await StaticResources.DockerfileDotnet10Isolated,
            _ => await StaticResources.DockerfileDotnetIsolated
        };

        await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", content);
    }

    private async Task InitDockerfileInProc(bool csx, string tfm)
    {
        if (csx)
        {
            await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileCsxDotNet);
        }
        else if (tfm == TargetFramework.Net8)
        {
            await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotNet8);
        }
        else
        {
            await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotNet);
        }
    }

    private static async Task ShowEolMessageIfNeeded(WorkerRuntime runtime, string tfm)
    {
        try
        {
            if (!WorkerRuntimeLanguageHelper.IsDotnetIsolated(runtime) || tfm == TargetFramework.Net8)
            {
                return;
            }

            var major = StacksApiHelper.GetMajorDotnetVersionFromDotnetVersionInProject(tfm);
            if (major == null)
            {
                return;
            }

            var stacksContent = await StaticResources.StacksJson;
            var stacks = Newtonsoft.Json.JsonConvert.DeserializeObject<FunctionsStacks>(stacksContent);
            var settings = stacks.GetRuntimeSettings(major.Value, out _);
            if (settings == null)
            {
                return;
            }

            if (settings.IsDeprecated == true || settings.IsDeprecatedForRuntime == true)
            {
                var msg = EolMessages.GetAfterEolCreateMessageDotNet(major.ToString(), settings.EndOfLifeDate!.Value);
                Spectre.Console.AnsiConsole.WriteLine(New.OutputTheme.WarningColor(msg));
            }
            else if (StacksApiHelper.IsInNextSixMonths(settings.EndOfLifeDate))
            {
                var msg = EolMessages.GetEarlyEolCreateMessageForDotNet(major.ToString(), settings.EndOfLifeDate!.Value);
                Spectre.Console.AnsiConsole.WriteLine(New.OutputTheme.WarningColor(msg));
            }
        }
        catch
        { /* non-fatal */
        }
    }
}
