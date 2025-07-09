// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class GlobalCoreToolsSettings
    {
        private static WorkerRuntime s_currentWorkerRuntime;

        public static ProgrammingModel? CurrentProgrammingModel { get; set; }

        public static WorkerRuntime CurrentWorkerRuntime
        {
            get
            {
                if (s_currentWorkerRuntime == WorkerRuntime.None)
                {
                    ColoredConsole.Error.WriteLine(QuietWarningColor("Can't determine project language from files. Please use one of [--dotnet-isolated, --dotnet, --javascript, --typescript, --java, --python, --powershell, --custom]"));
                    throw new CliException($"Worker runtime cannot be '{WorkerRuntime.None}'. Please set a valid runtime.");
                }

                return s_currentWorkerRuntime;
            }

            set
            {
                s_currentWorkerRuntime = value;
            }
        }

        public static WorkerRuntime CurrentWorkerRuntimeOrNone
        {
            get
            {
                return s_currentWorkerRuntime;
            }
        }

        public static string CurrentLanguageOrNull { get; private set; } = null;

        public static void Init(ISecretsManager secretsManager, string[] args)
        {
            try
            {
                if (args.Contains("--csharp"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.Dotnet;
                    CurrentLanguageOrNull = "csharp";
                }
                else if (args.Contains("--dotnet"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.Dotnet;
                }
                else if (args.Contains("--dotnet-isolated"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.DotnetIsolated;
                }
                else if (args.Contains("--javascript"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.Node;
                    CurrentLanguageOrNull = "javascript";
                }
                else if (args.Contains("--typescript"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.Node;
                    CurrentLanguageOrNull = "typescript";
                }
                else if (args.Contains("--node"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.Node;
                }
                else if (args.Contains("--java"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.Java;
                }
                else if (args.Contains("--python"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.Python;
                }
                else if (args.Contains("--powershell"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.Powershell;
                }
                else if (args.Contains("--custom"))
                {
                    s_currentWorkerRuntime = WorkerRuntime.Custom;
                }
                else
                {
                    s_currentWorkerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(secretsManager);
                }
            }
            catch
            {
                s_currentWorkerRuntime = WorkerRuntime.None;
            }
        }

        // Test helper method to set _currentWorkerRuntime for testing purpose
        internal static void SetWorkerRuntime(WorkerRuntime workerRuntime)
        {
            s_currentWorkerRuntime = workerRuntime;
        }
    }
}
