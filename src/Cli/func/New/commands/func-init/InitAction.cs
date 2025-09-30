// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.New;
using Colors.Net;

namespace Azure.Functions.Cli.Commands.Init;

public class InitAction : IAction
{
    public string Name => "init";

    public string Description => "Create a new Function App and initializes a git repository.";

    public WorkerRuntime ResolvedWorkerRuntime { get; set; }

    public string ResolvedLanguage { get; set; }

    public ProgrammingModel ResolvedProgrammingModel { get; set; }

    public async Task<int> Run(ParseResult parseResult, CancellationToken cancellationToken)
    {
        Utilities.WarnIfPreviewVersion();
        Utilities.PrintSupportInformation();

        var folderName = parseResult.GetValue(InitCommandParser.FolderNameArgument);
        if (!string.IsNullOrEmpty(folderName))
        {
            var folderPath = Path.Combine(Environment.CurrentDirectory, folderName);
            FileSystemHelpers.EnsureDirectory(folderPath);
            Environment.CurrentDirectory = folderPath;
        }

        var runtime = ResolveWorkerRuntime(parseResult);

        try
        {
            var workerRuntimeInitalizer = ProjectInitializerFactory.Get(runtime);

            if (parseResult.GetValue(InitCommandParser.DockerOnlyOption))
            {
                await workerRuntimeInitalizer.WriteDockerfileAsync(parseResult, cancellationToken);
                return 0;
            }

            await workerRuntimeInitalizer.InitializeAsync(parseResult, cancellationToken);

            await InitCommon.WriteExtensionsJsonAsync(cancellationToken);

            if (parseResult.GetValue(InitCommandParser.SourceControlOption))
            {
                await InitCommon.SetupSourceControlAsync(cancellationToken);
            }

            if (parseResult.GetValue(InitCommandParser.DockerOption))
            {
                await workerRuntimeInitalizer.WriteDockerfileAsync(parseResult, cancellationToken);
            }

            await workerRuntimeInitalizer.PostInstallAsync(parseResult, cancellationToken);

            return 0;
        }
        catch (Exception exception)
        {
            Spectre.Console.AnsiConsole.WriteException(exception);
            return 1;
        }
    }

    private static WorkerRuntime ResolveWorkerRuntime(ParseResult parse)
    {
        // Prefer explicit flag
        var explicitRuntime = parse.GetValue(InitCommandParser.WorkerRuntimeOption);
        if (explicitRuntime != WorkerRuntime.None)
        {
            return explicitRuntime;
        }

        // Then previously configured runtime
        if (GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone != WorkerRuntime.None)
        {
            return GlobalCoreToolsSettings.CurrentWorkerRuntime;
        }

        var runtime = New.SelectionMenuHelper.AskRuntime();
        Spectre.Console.AnsiConsole.MarkupLine(New.OutputTheme.TitleColor(WorkerRuntimeLanguageHelper.GetRuntimeMoniker(runtime)));
        return runtime;
    }
}
