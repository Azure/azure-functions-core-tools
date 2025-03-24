using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit.Abstractions;
using static Microsoft.Azure.AppService.Proxy.Runtime.Trace;

namespace Cli.Core.E2E.Tests.Fixtures
{
    public abstract class BaseFunctionAppFixture : IAsyncLifetime
    {
        public ITestOutputHelper Log { get; set; }
        public string FuncPath { get; set; }
        public string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        public bool CleanupWorkingDirectory { get; set; } = true;
        public string WorkerRuntime {  get; set; }
        public string? TargetFramework { get; set; }
        public string? Version { get ; set; }

        public BaseFunctionAppFixture(string workerRuntime, string? targetFramework = null, string? version = null)
        {
            WorkerRuntime = workerRuntime;
            TargetFramework = targetFramework;
            Version = version;

            Log = new Mock<ITestOutputHelper>().Object;

            FuncPath = Environment.GetEnvironmentVariable("FUNC_PATH");

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

        /// <summary>
        /// Uninstalls a dotnet template package with the specified name
        /// </summary>
        /// <param name="templatePackageName">The name of the template package to uninstall</param>
        /// <returns>True if the uninstallation was successful, false otherwise</returns>
        public bool UninstallDotnetTemplate(string templatePackageName)
        {
            if (string.IsNullOrEmpty(templatePackageName))
            {
                throw new ArgumentException("Template package name cannot be null or empty", nameof(templatePackageName));
            }

            Log?.WriteLine($"Uninstalling dotnet template package: {templatePackageName}");

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
                    Log?.WriteLine($"[dotnet template --uninstall] {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                    Log?.WriteLine($"[dotnet template --uninstall error] {e.Data}");
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
                    Log?.WriteLine($"Failed to uninstall template package '{templatePackageName}'. Exit code: {exitCode}");
                    Log?.WriteLine($"Error: {errorBuilder}");
                    return false;
                }

                Log?.WriteLine($"Successfully uninstalled template package: {templatePackageName}");
                return true;
            }
            catch (Exception ex)
            {
                Log?.WriteLine($"Exception occurred while uninstalling template package '{templatePackageName}': {ex.Message}");
                return false;
            }
        }

        public Task DisposeAsync()
        {
            Directory.Delete(WorkingDirectory, true);
            return Task.CompletedTask;
        }

        public async Task InitializeAsync()
        {
            // Create the function
            var initArgs = new List<string> { ".", "--worker-runtime", WorkerRuntime }
                .Concat(TargetFramework != null
                    ? new[] { "--target-framework", TargetFramework }
                    : Array.Empty<string>())
                .Concat(Version != null
                    ? new[] { "-m", Version }
                    : Array.Empty<string>())
                .ToList();

            var funcInitResult = new FuncInitCommand(FuncPath, Log)
                                    .WithWorkingDirectory(WorkingDirectory)
                                    .Execute(initArgs);

            if (funcInitResult.ExitCode != 0)
            {
                throw new Exception($"Failed to initialize function app: {funcInitResult.StdErr}");
            }

            await RetryHelper.RetryAsync(() =>
            {
                // Add Http Trigger
                var funcNewResult = new FuncNewCommand(FuncPath, Log)
                                    .WithWorkingDirectory(WorkingDirectory)
                                    .Execute(new List<string> { "--template", "HttpTrigger", "--name", "HttpTrigger" });


                if (funcNewResult.ExitCode != 0)
                {
                    throw new Exception($"Failed to add HTTP trigger: {funcNewResult.StdErr}");
                }
                return Task.FromResult(true);

            });
            
        }
    }
}
