// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    /// <summary>
    /// Abstract base class for Python package installers.
    /// </summary>
    internal abstract class PythonPackageInstaller
    {
        protected string FunctionAppRoot { get; }
        protected string PackagesLocation { get; }

        protected PythonPackageInstaller(string functionAppRoot, string packagesLocation)
        {
            FunctionAppRoot = functionAppRoot;
            PackagesLocation = packagesLocation;
        }

        /// <summary>
        /// Gets the name of the tool (e.g., "pip", "poetry", "uv").
        /// </summary>
        public abstract string ToolName { get; }

        /// <summary>
        /// Checks if the tool is installed on the system.
        /// </summary>
        public virtual bool IsToolInstalled()
        {
            return CommandChecker.CommandExists(ToolName);
        }

        /// <summary>
        /// Ensures the tool is installed, throwing an exception if not.
        /// </summary>
        public virtual void EnsureToolInstalled()
        {
            EnsureToolInstalled(GetInstallationMessage());
        }

        /// <summary>
        /// Ensures the tool is installed with a custom message, throwing an exception if not.
        /// </summary>
        public virtual void EnsureToolInstalled(string customMessage)
        {
            if (!IsToolInstalled())
            {
                throw new CliException($"{ToolName} is not installed. {customMessage}");
            }
        }

        /// <summary>
        /// Gets the message to display when the tool is not installed.
        /// </summary>
        protected abstract string GetInstallationMessage();

        /// <summary>
        /// Restores Python dependencies to the specified packages location.
        /// </summary>
        public abstract Task RestoreDependenciesAsync(WorkerLanguageVersionInfo pythonWorkerInfo);

        /// <summary>
        /// Exports dependencies to a requirements.txt file for Docker builds.
        /// </summary>
        public virtual Task ExportToRequirementsTxtAsync(string outputPath)
        {
            throw new NotSupportedException($"{ToolName} does not support exporting to requirements.txt");
        }

        /// <summary>
        /// Downloads packages using pip given a requirements file.
        /// </summary>
        protected async Task DownloadPackagesWithPipAsync(string pythonExe, string requirementsTxtPath)
        {
            var pipExe = new Executable(pythonExe, $"-m pip download -r \"{requirementsTxtPath}\" --dest \"{PackagesLocation}\"");
            var sbPipErrors = new StringBuilder();

            ColoredConsole.WriteLine($"{pythonExe} -m pip download -r {requirementsTxtPath} --dest {PackagesLocation}");
            var pipExitCode = await pipExe.RunAsync(o => ColoredConsole.WriteLine(o), e => sbPipErrors.AppendLine(e));

            if (pipExitCode != 0)
            {
                throw new CliException("There was an error downloading dependencies. " + sbPipErrors.ToString());
            }
        }
    }

    /// <summary>
    /// Pip-based package installer.
    /// </summary>
    internal class PipInstaller : PythonPackageInstaller
    {
        public PipInstaller(string functionAppRoot, string packagesLocation)
            : base(functionAppRoot, packagesLocation)
        {
        }

        public override string ToolName => "pip";

        protected override string GetInstallationMessage()
        {
            return "Please install pip to use requirements.txt for dependency management.";
        }

        public override async Task RestoreDependenciesAsync(WorkerLanguageVersionInfo pythonWorkerInfo)
        {
            var pythonExe = pythonWorkerInfo.ExecutablePath;
            var requirementsTxt = Path.Combine(FunctionAppRoot, Constants.RequirementsTxt);

            await DownloadPackagesWithPipAsync(pythonExe, requirementsTxt);
        }
    }

    /// <summary>
    /// Poetry-based package installer.
    /// </summary>
    internal class PoetryInstaller : PythonPackageInstaller
    {
        public PoetryInstaller(string functionAppRoot, string packagesLocation)
            : base(functionAppRoot, packagesLocation)
        {
        }

        public override string ToolName => "poetry";

        protected override string GetInstallationMessage()
        {
            return "Please install poetry to use pyproject.toml for dependency management. " +
                   "Alternatively, generate a requirements.txt file from your pyproject.toml.";
        }

        public override async Task RestoreDependenciesAsync(WorkerLanguageVersionInfo pythonWorkerInfo)
        {
            EnsureToolInstalled();

            var tempRequirementsTxt = Path.Combine(Path.GetTempPath(), $"requirements-{Guid.NewGuid()}.txt");
            try
            {
                await ExportToRequirementsTxtAsync(tempRequirementsTxt);
                await DownloadPackagesWithPipAsync(pythonWorkerInfo.ExecutablePath, tempRequirementsTxt);
            }
            finally
            {
                if (FileSystemHelpers.FileExists(tempRequirementsTxt))
                {
                    FileSystemHelpers.FileDelete(tempRequirementsTxt);
                }
            }
        }

        public override async Task ExportToRequirementsTxtAsync(string outputPath)
        {
            EnsureToolInstalled();

            var poetryExe = new Executable("poetry", $"export -f requirements.txt --output \"{outputPath}\" --without-hashes", workingDirectory: FunctionAppRoot);
            var sbErrors = new StringBuilder();

            ColoredConsole.WriteLine($"poetry export -f requirements.txt --output {outputPath} --without-hashes");
            var exitCode = await poetryExe.RunAsync(o => ColoredConsole.WriteLine(o), e => sbErrors.AppendLine(e));

            if (exitCode != 0)
            {
                throw new CliException("There was an error exporting dependencies from poetry. " + sbErrors.ToString());
            }
        }
    }

    /// <summary>
    /// UV-based package installer.
    /// </summary>
    internal class UvInstaller : PythonPackageInstaller
    {
        public UvInstaller(string functionAppRoot, string packagesLocation)
            : base(functionAppRoot, packagesLocation)
        {
        }

        public override string ToolName => "uv";

        protected override string GetInstallationMessage()
        {
            return "Please install uv to use uv.lock for dependency management. " +
                   "Alternatively, generate a requirements.txt file from your pyproject.toml.";
        }

        public override async Task RestoreDependenciesAsync(WorkerLanguageVersionInfo pythonWorkerInfo)
        {
            EnsureToolInstalled();

            var tempRequirementsTxt = Path.Combine(Path.GetTempPath(), $"requirements-{Guid.NewGuid()}.txt");
            try
            {
                await ExportToRequirementsTxtAsync(tempRequirementsTxt);
                await DownloadPackagesWithPipAsync(pythonWorkerInfo.ExecutablePath, tempRequirementsTxt);
            }
            finally
            {
                if (FileSystemHelpers.FileExists(tempRequirementsTxt))
                {
                    FileSystemHelpers.FileDelete(tempRequirementsTxt);
                }
            }
        }

        public override async Task ExportToRequirementsTxtAsync(string outputPath)
        {
            EnsureToolInstalled();

            var uvExe = new Executable("uv", $"export --format requirements-txt --output-file \"{outputPath}\" --no-hashes", workingDirectory: FunctionAppRoot);
            var sbErrors = new StringBuilder();

            ColoredConsole.WriteLine($"uv export --format requirements-txt --output-file {outputPath} --no-hashes");
            var exitCode = await uvExe.RunAsync(o => ColoredConsole.WriteLine(o), e => sbErrors.AppendLine(e));

            if (exitCode != 0)
            {
                throw new CliException("There was an error exporting dependencies from uv. " + sbErrors.ToString());
            }
        }
    }
}
