using System;
using System.Linq;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class GlobalCoreToolsSettings
    {
        private static WorkerRuntime _currentWorkerRuntime;

        public static WorkerRuntime CurrentWorkerRuntime
        {
            get
            {
                if (_currentWorkerRuntime == WorkerRuntime.None)
                {
                    ColoredConsole.Error.WriteLine(QuietWarningColor("Can't determine project language from files. Please use one of [--csharp, --javascript, --typescript, --java, --python, --powershell, --custom]"));
                }
                return _currentWorkerRuntime;
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
                    _currentWorkerRuntime = WorkerRuntime.dotnet;
                    CurrentLanguageOrNull = "csharp";
                }
                else if (args.Contains("--dotnet"))
                {
                    _currentWorkerRuntime = WorkerRuntime.dotnet;
                }
                else if (args.Contains("--javascript"))
                {
                    _currentWorkerRuntime = WorkerRuntime.node;
                    CurrentLanguageOrNull = "javascript";
                }
                else if (args.Contains("--typescript"))
                {
                    _currentWorkerRuntime = WorkerRuntime.node;
                    CurrentLanguageOrNull = "typescript";
                }
                else if (args.Contains("--node"))
                {
                    _currentWorkerRuntime = WorkerRuntime.node;
                }
                else if (args.Contains("--java"))
                {
                    _currentWorkerRuntime = WorkerRuntime.java;
                }
                else if (args.Contains("--python"))
                {
                    _currentWorkerRuntime = WorkerRuntime.python;
                }
                else if (args.Contains("--powershell"))
                {
                    _currentWorkerRuntime = WorkerRuntime.powershell;
                }
                else if(args.Contains("--custom"))
                {
                    _currentWorkerRuntime = WorkerRuntime.custom;
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
    }
}