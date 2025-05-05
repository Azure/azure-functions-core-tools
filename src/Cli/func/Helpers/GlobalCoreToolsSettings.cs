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
        private static WorkerRuntime _currentWorkerRuntime;

        public static ProgrammingModel? CurrentProgrammingModel { get; set; }

        public static WorkerRuntime CurrentWorkerRuntime
        {
            get
            {
                if (_currentWorkerRuntime == WorkerRuntime.None)
                {
                    ColoredConsole.Error.WriteLine(QuietWarningColor("Can't determine project language from files. Please use one of [--dotnet-isolated, --dotnet, --javascript, --typescript, --java, --python, --powershell, --custom]"));
                    throw new CliException($"Worker runtime cannot be '{WorkerRuntime.None}'. Please set a valid runtime.");
                }

                return _currentWorkerRuntime;
            }

            set
            {
                _currentWorkerRuntime = value;
            }
        }

        public static WorkerRuntime CurrentWorkerRuntimeOrNone
        {
            get
            {
                return _currentWorkerRuntime;
            }
        }

        public static string CurrentLanguageOrNull { get; private set; } = null;

        public static void Init(ISecretsManager secretsManager, string[] args)
        {
            try
            {
                if (args.Contains("--csharp"))
                {
                    _currentWorkerRuntime = WorkerRuntime.Dotnet;
                    CurrentLanguageOrNull = "csharp";
                }
                else if (args.Contains("--dotnet"))
                {
                    _currentWorkerRuntime = WorkerRuntime.Dotnet;
                }
                else if (args.Contains("--dotnet-isolated"))
                {
                    _currentWorkerRuntime = WorkerRuntime.DotnetIsolated;
                }
                else if (args.Contains("--javascript"))
                {
                    _currentWorkerRuntime = WorkerRuntime.Node;
                    CurrentLanguageOrNull = "javascript";
                }
                else if (args.Contains("--typescript"))
                {
                    _currentWorkerRuntime = WorkerRuntime.Node;
                    CurrentLanguageOrNull = "typescript";
                }
                else if (args.Contains("--node"))
                {
                    _currentWorkerRuntime = WorkerRuntime.Node;
                }
                else if (args.Contains("--java"))
                {
                    _currentWorkerRuntime = WorkerRuntime.Java;
                }
                else if (args.Contains("--python"))
                {
                    _currentWorkerRuntime = WorkerRuntime.Python;
                }
                else if (args.Contains("--powershell"))
                {
                    _currentWorkerRuntime = WorkerRuntime.Powershell;
                }
                else if (args.Contains("--custom"))
                {
                    _currentWorkerRuntime = WorkerRuntime.Custom;
                }
                else
                {
                    _currentWorkerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(secretsManager);
                }
            }
            catch
            {
                _currentWorkerRuntime = WorkerRuntime.None;
            }
        }

        // Test helper method to set _currentWorkerRuntime for testing purpose
        internal static void SetWorkerRuntime(WorkerRuntime workerRuntime)
        {
            _currentWorkerRuntime = workerRuntime;
        }
    }
}
