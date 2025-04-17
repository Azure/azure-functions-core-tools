// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Func.TestFramework.Helpers;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Func.E2ETests.Fixtures
{
    public abstract class BaseFunctionAppFixture : IAsyncLifetime
    {
        public BaseFunctionAppFixture(string workerRuntime, string? targetFramework = null, string? version = null)
        {
            WorkerRuntime = workerRuntime;
            TargetFramework = targetFramework;
            Version = version;

            Log = new Mock<ITestOutputHelper>().Object;

            FuncPath = Environment.GetEnvironmentVariable(Constants.FuncPath);

            if (FuncPath == null)
            {
                // Fallback for local testing in Visual Studio, etc.
                FuncPath = Path.Combine(Environment.CurrentDirectory, "func");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    FuncPath += ".exe";
                }

                if (!File.Exists(FuncPath))
                {
                    throw new ApplicationException("Could not locate the 'func' executable to use for testing. Make sure the FUNC_PATH environment variable is set to the full path of the func executable.");
                }
            }

            Directory.CreateDirectory(WorkingDirectory);
        }

        public ITestOutputHelper Log { get; set; }

        public string? FuncPath { get; set; }

        public string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public bool CleanupWorkingDirectory { get; set; } = true;

        public string WorkerRuntime { get; set; }

        public string? TargetFramework { get; set; }

        public string? Version { get; set; }

        /// <summary>
        /// Uninstalls a dotnet template package with the specified name.
        /// </summary>
        /// <param name="templatePackageName">The name of the template package to uninstall.</param>
        /// <returns>True if the uninstallation was successful, false otherwise.</returns>
        public bool UninstallDotnetTemplate(string templatePackageName)
        {
            if (string.IsNullOrEmpty(templatePackageName))
            {
                throw new ArgumentException("Template package name cannot be null or empty", nameof(templatePackageName));
            }

            Log.WriteLine($"Uninstalling dotnet template package: {templatePackageName}");

            // Create a new process to run the dotnet uninstall command
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"new --uninstall {templatePackageName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = WorkingDirectory
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                    Log.WriteLine($"[dotnet template --uninstall] {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    Log.WriteLine($"[dotnet template --uninstall error] {e.Data}");
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                var exitCode = process.ExitCode;

                if (exitCode != 0)
                {
                    Log.WriteLine($"Failed to uninstall template package '{templatePackageName}'. Exit code: {exitCode}");
                    Log.WriteLine($"Error: {errorBuilder}");
                    return false;
                }

                Log.WriteLine($"Successfully uninstalled template package: {templatePackageName}");
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Exception occurred while uninstalling template package '{templatePackageName}': {ex.Message}");
                return false;
            }
        }

        public Task DisposeAsync()
        {
            try
            {
                Directory.Delete(WorkingDirectory, true);
            }
            catch
            {
                // Ignore any errors when cleaning up
            }

            return Task.CompletedTask;
        }

        public async Task InitializeAsync()
        {
            var initArgs = new List<string> { ".", "--worker-runtime", WorkerRuntime }
                .Concat(TargetFramework != null
                    ? new[] { "--target-framework", TargetFramework }
                    : Array.Empty<string>())
                .Concat(Version != null
                    ? new[] { "-m", Version }
                    : Array.Empty<string>())
                .ToList();

            string nameOfFixture = WorkerRuntime + (TargetFramework ?? string.Empty) + (Version ?? string.Empty);

            await FunctionAppSetupHelper.FuncInitWithRetryAsync(FuncPath, nameOfFixture, WorkingDirectory, Log, initArgs);

            var funcNewArgs = new[] { ".", "--template", "HttpTrigger", "--name", "HttpTrigger" }
                                .Concat(!WorkerRuntime.Contains("dotnet") ? new[] { "--language", WorkerRuntime } : Array.Empty<string>())
                                .ToArray();
            await FunctionAppSetupHelper.FuncNewWithRetryAsync(FuncPath, nameOfFixture, WorkingDirectory, Log, funcNewArgs, WorkerRuntime);

            // Enable worker indexing to maximize probability of function being found
            string localSettingsJson = Path.Combine(WorkingDirectory, "local.settings.json");

            // Read the existing JSON file
            string json = File.ReadAllText(localSettingsJson);

            // Parse the JSON
            JObject settings = JObject.Parse(json);

            // Add the new setting
            settings["AzureWebJobsFeatureFlags"] = "EnableWorkerIndexing";

            // Write the updated content back to the file
            File.WriteAllText(localSettingsJson, settings.ToString());
        }
    }
}
