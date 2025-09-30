// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Commands.Init;

public sealed class NodeProjectInitializer : IProjectInitializer
{
    public WorkerRuntime Runtime => WorkerRuntime.Node;

    public string ResolvedProgrammingLanguage { get; set; }

    public ProgrammingModel ResolvedProgrammingModel { get; set; }

    public async Task InitializeAsync(ParseResult args, CancellationToken ct)
    {
        var language = args.GetValue(InitCommandParser.LanguageOption);
        var modelOpt = args.GetValue(InitCommandParser.ModelOption);

        ResolvedProgrammingLanguage = EnsureLanguage(language);
        ResolvedProgrammingModel = ProgrammingModelHelper.ResolveProgrammingModel(modelOpt, Runtime, ResolvedProgrammingLanguage);

        await NodeJSHelpers.SetupProject(ResolvedProgrammingModel, language);
        await WriteFilesAsync();
        await WriteHostJsonAsync(ResolvedProgrammingModel);
        await WriteLocalSettingsJsonAsync(ResolvedProgrammingModel);
    }

    public async Task WriteDockerfileAsync(ParseResult args, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ResolvedProgrammingLanguage))
        {
            ResolvedProgrammingLanguage = EnsureLanguage(args.GetValue(InitCommandParser.LanguageOption));
        }

        if (ResolvedProgrammingLanguage == Constants.Languages.TypeScript)
        {
            await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileTypeScript);
        }
        else
        {
            await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileJavaScript);
        }

        await FileSystemHelpers.WriteFileIfNotExists(".dockerignore", await StaticResources.DockerIgnoreFile);
    }

    public async Task PostInstallAsync(ParseResult args, CancellationToken ct)
    {
        if (args.GetValue(InitCommandParser.SkipNpmInstallOption))
        {
            Spectre.Console.AnsiConsole.Write(New.OutputTheme.AdditionalInfoColor("You skipped \"npm install\". Run it manually."));
            return;
        }

        if (ResolvedProgrammingModel == ProgrammingModel.V4)
        {
            try
            {
                await NpmHelper.Install();
            }
            catch
            {
                Spectre.Console.AnsiConsole.WriteLine(New.OutputTheme.WarningColor("The npm install step failed. Please run \"npm install\" in the project folder to install required packages."));
            }
        }
    }

    private static string EnsureLanguage(string language)
    {
        if (!string.IsNullOrEmpty(language))
        {
            return language;
        }

        var result = New.SelectionMenuHelper.AskLanguage();
        Spectre.Console.AnsiConsole.MarkupLine(New.OutputTheme.TitleColor(result));

        // Fallback to JavaScript if no languages are found for Node
        return result ?? Constants.Languages.JavaScript;
    }

    private static async Task WriteFilesAsync()
    {
        foreach (var pair in InitCommon.FileToContentMap)
        {
            await FileSystemHelpers.WriteFileIfNotExists(pair.Key.Value, await pair.Value);
        }
    }

    private static async Task WriteHostJsonAsync(ProgrammingModel model)
    {
        var host = await StaticResources.HostJson;
        if (model == ProgrammingModel.V4)
        {
            host = await host.AppendContent(Constants.ExtensionBundleConfigPropertyName, StaticResources.BundleConfigNodeV4);
        }
        else
        {
            host = await host.AppendContent(Constants.ExtensionBundleConfigPropertyName, StaticResources.BundleConfig);
        }

        await FileSystemHelpers.WriteFileIfNotExists(Constants.HostJsonFileName, host);
    }

    private static async Task WriteLocalSettingsJsonAsync(ProgrammingModel model)
    {
        var content = await StaticResources.LocalSettingsJson;
        content = content.Replace($"{{{Constants.FunctionsWorkerRuntime}}}", WorkerRuntimeLanguageHelper.GetRuntimeMoniker(WorkerRuntime.Node));
        var storage = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Constants.StorageEmulatorConnectionString : string.Empty;
        content = content.Replace($"{{{Constants.AzureWebJobsStorage}}}", storage);
        await FileSystemHelpers.WriteFileIfNotExists("local.settings.json", content);
    }
}
