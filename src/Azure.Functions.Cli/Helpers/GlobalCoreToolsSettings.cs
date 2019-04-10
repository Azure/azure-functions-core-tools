using System;
using System.Linq;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Helpers
{
    public static class GlobalCoreToolsSettings
    {
        private static WorkerRuntime _currentWorkerRuntime;
        public static WorkerRuntime CurrentWorkerRuntime
        {
            get
            {
                if (_currentWorkerRuntime != WorkerRuntime.None)
                {
                    return _currentWorkerRuntime;
                }
                throw new Exception("Can't determin project langauge from files. Please use one of [--csharp, --javascript, --typescript, --java, --python, --powershell]");
            }
        }

        public static void Init(ISecretsManager secretsManager, string[] args)
        {
            try
            {
                if (args.Contains("--csharp") || args.Contains("--dotnet"))
                {
                    _currentWorkerRuntime = WorkerRuntime.dotnet;
                }
                else if (args.Contains("--javascript") || args.Contains("--typescript") || args.Contains("--node"))
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