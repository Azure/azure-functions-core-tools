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
        private static bool _isHelpRunning;
        private static bool _isVerbose;
        private static bool _explicitOffline;
        private static Lazy<bool> _networkOffline = new Lazy<bool>(() => false);

        public static bool IsHelpRunning => _isHelpRunning;

        public static bool IsVerbose => _isVerbose;

        /// <summary>
        /// Gets a value indicating whether the CLI is currently in offline mode.
        /// Returns true immediately if the user explicitly requested offline mode
        /// (via --offline flag or FUNCTIONS_CORE_TOOLS_OFFLINE env var).
        /// Otherwise, lazily performs a one-time network probe on first access so
        /// commands that never check offline state (e.g. func --help) pay no cost.
        /// </summary>
        public static bool IsOfflineMode => _explicitOffline || _networkOffline.Value;

        public static ProgrammingModel? CurrentProgrammingModel { get; set; }

        public static WorkerRuntime CurrentWorkerRuntime
        {
            get
            {
                if (_currentWorkerRuntime == WorkerRuntime.None)
                {
                    ColoredConsole.Error.WriteLine(QuietWarningColor("Can't determine project language from files. Please use one of [--dotnet-isolated, --dotnet, --javascript, --typescript, --python, --powershell, --custom]"));
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
            _isVerbose = args.Contains("--verbose");
            _explicitOffline = args.Contains("--offline") || EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.FunctionsCoreToolsOffline);

            // Lazy network probe — only runs on first access of IsOfflineMode when no explicit offline flag was set
            _networkOffline = new Lazy<bool>(() =>
                _explicitOffline
                    ? true
                    : Task.Run(() => OfflineHelper.IsOfflineAsync()).GetAwaiter().GetResult());

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

        internal static void SetIsHelpRunning(bool value)
        {
            _isHelpRunning = value;
        }

        /// <summary>
        /// Sets the explicit offline state and resets the lazy network probe.
        /// Called by <see cref="OfflineHelper"/> when offline state changes.
        /// </summary>
        internal static void SetOffline(bool isOffline)
        {
            _explicitOffline = isOffline;
            _networkOffline = new Lazy<bool>(() => isOffline);
        }

        // Test helper method to set _currentWorkerRuntime for testing purpose
        internal static void SetWorkerRuntime(WorkerRuntime workerRuntime)
        {
            _currentWorkerRuntime = workerRuntime;
        }
    }
}
