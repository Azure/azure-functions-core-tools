// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class DotnetHelpersTests : IDisposable
    {
        private readonly string _originalHiveFlag = Environment.GetEnvironmentVariable(DotnetHelpers.CustomHiveFlag);
        private readonly string _originalHiveRoot = Environment.GetEnvironmentVariable(DotnetHelpers.CustomHiveRoot);
        private readonly string _originalHiveKey = Environment.GetEnvironmentVariable(DotnetHelpers.CustomHiveKey);
        private readonly Func<string, Task<int>> _originalRunDotnetNew = DotnetHelpers.RunDotnetNewFunc;
        private readonly string _hiveRoot = Path.Combine(Path.GetTempPath(), $"func-hive-tests-{Guid.NewGuid():N}");

        public DotnetHelpersTests()
        {
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveFlag, null);
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveRoot, null);
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveKey, null);
        }

        public void Dispose()
        {
            DotnetHelpers.RunDotnetNewFunc = _originalRunDotnetNew;
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveFlag, _originalHiveFlag);
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveRoot, _originalHiveRoot);
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveKey, _originalHiveKey);
            if (Directory.Exists(_hiveRoot))
            {
                Directory.Delete(_hiveRoot, recursive: true);
            }
        }

        [Theory]
        [InlineData("net10.0")]
        [InlineData("net11.0")]
        [InlineData("net11.0-windows")]
        public void TargetFrameworkRegex_RecognizesModernFrameworks(string targetFramework)
        {
            var output = $"{{\"Properties\":{{\"TargetFrameworks\":\"\",\"TargetFramework\":\"{targetFramework}\"}}}}";
            var matches = TargetFrameworkHelper.TfmRegex.Matches(output);

            Assert.Single(matches);
            Assert.Equal(targetFramework, matches[0].Value);
        }

        [Fact]
        public void SupportedFrameworks_Net11IsIsolatedOnly()
        {
            Assert.Contains("net11.0", TargetFrameworkHelper.GetSupportedTargetFrameworks());
            Assert.Equal(new[] { "net8.0", "net6.0" }, TargetFrameworkHelper.GetSupportedInProcTargetFrameworks());
        }

        [Fact]
        public void EnsureDotnet_DoesNotThrow_WhenDotnetExists()
        {
            // dotnet is always installed in the test environment
            var exception = Record.Exception(() => DotnetHelpers.EnsureDotnet());
            Assert.Null(exception);
        }

        [Theory]
        [InlineData("BlobTrigger", "blob")]
        [InlineData("HttpTrigger", "http")]
        [InlineData("TimerTrigger", "timer")]
        [InlineData("DaprPublishOutputBinding", null)]
        [InlineData("DaprServiceInvocationTrigger", null)]
        [InlineData("DaprTopicTrigger", null)]
        [InlineData("UnknownTrigger", null)]
        public void GetTemplateShortName_ReturnsExpectedShortName(string input, string expected)
        {
            if (expected != null)
            {
                var result = DotnetHelpers.GetTemplateShortName(input);
                Assert.Equal(expected, result);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => DotnetHelpers.GetTemplateShortName(input));
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, 17)]
        [InlineData(WorkerRuntime.DotnetIsolated, 16)]
        public void GetTemplates_ReturnsExpectedTemplates(WorkerRuntime runtime, int expectedCount)
        {
            var templates = DotnetHelpers.GetTemplates(runtime);
            Assert.Equal(expectedCount, templates.Count());
            Assert.DoesNotContain(templates, template =>
                template.Contains("Dapr", StringComparison.OrdinalIgnoreCase) ||
                template.Contains("OpenAI", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, "")]
        [InlineData(WorkerRuntime.DotnetIsolated, "net-isolated")]
        public async Task TemplateOperationAsync_Isolated_InstallsAndUninstalls_InOrder(WorkerRuntime workerRuntime, string path)
        {
            // Arrange
            var calls = new List<string>();
            var original = DotnetHelpers.RunDotnetNewFunc;
            try
            {
                DotnetHelpers.RunDotnetNewFunc = args =>
                {
                    calls.Add(args);
                    return Task.FromResult(0);
                };

                bool actionCalled = false;
                Func<Task> action = () =>
                {
                    actionCalled = true;
                    return Task.CompletedTask;
                };

                // Act
                await DotnetHelpers.TemplateOperationAsync(action, workerRuntime);

                // Assert
                Assert.True(actionCalled);
                var uninstallCalls = calls.Where(a => a.Contains("new uninstall", StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(uninstallCalls.Count >= 4, $"Expected at least 4 uninstall calls, got {uninstallCalls.Count}");

                // Check for at least 2 install calls with correct template path
                var installCalls = calls.Where(a => a.Contains("new install", StringComparison.OrdinalIgnoreCase) &&
                                                   a.Contains(Path.Combine("templates", path), StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(installCalls.Count >= 2, $"Expected at least 2 install calls with '{Path.Combine("templates", path)}', got {installCalls.Count}");

                // Verify the sequence: first 2 should be uninstalls
                Assert.Contains("new uninstall", calls[0], StringComparison.OrdinalIgnoreCase);
                Assert.Contains("new uninstall", calls[1], StringComparison.OrdinalIgnoreCase);

                // Find the last 2 calls and verify they are uninstalls
                var lastTwoCalls = calls.TakeLast(2).ToList();
                Assert.True(
                    lastTwoCalls.All(call => call.Contains("new uninstall", StringComparison.OrdinalIgnoreCase)),
                    "Last 2 calls should be uninstall operations");
            }
            finally
            {
                DotnetHelpers.RunDotnetNewFunc = original;
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet)]
        [InlineData(WorkerRuntime.DotnetIsolated)]
        public void GetTemplateCreationOptions_DisablesUpdatesOnlyForCustomHives(WorkerRuntime workerRuntime)
        {
            Assert.Empty(DotnetHelpers.GetTemplateCreationOptions(workerRuntime));

            string hive = EnableCustomHive(workerRuntime);
            string options = DotnetHelpers.GetTemplateCreationOptions(workerRuntime);

            Assert.Equal($" --debug:custom-hive \"{hive}\" --no-update-check", options);
            Assert.DoesNotContain("--no-restore", options);
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet)]
        [InlineData(WorkerRuntime.DotnetIsolated)]
        public async Task TemplateOperationAsync_CustomHive_DoesNotTrustPartialPackages(WorkerRuntime workerRuntime)
        {
            string hive = EnableCustomHive(workerRuntime);
            CreateHivePackage(hive);
            var calls = new List<string>();
            DotnetHelpers.RunDotnetNewFunc = args =>
            {
                calls.Add(args);
                Assert.False(File.Exists(Path.Combine(hive, DotnetHelpers.CustomHiveReadyFileName)));
                return Task.FromResult(0);
            };

            int actions = 0;
            Task Action()
            {
                Assert.True(File.Exists(Path.Combine(hive, DotnetHelpers.CustomHiveReadyFileName)));
                actions++;
                return Task.CompletedTask;
            }

            await DotnetHelpers.TemplateOperationAsync(Action, workerRuntime);
            int installCount = calls.Count;
            await DotnetHelpers.TemplateOperationAsync(Action, workerRuntime);

            Assert.True(installCount >= 2);
            Assert.Equal(installCount, calls.Count);
            Assert.Equal(2, actions);
            Assert.All(calls, args =>
            {
                Assert.Contains("new install", args);
                Assert.Contains("--force", args);
                Assert.DoesNotContain("--no-update-check", args);
            });
        }

        [Fact]
        public async Task TemplateOperationAsync_CustomHive_WaitsForConcurrentInstallation()
        {
            string hive = EnableCustomHive(WorkerRuntime.DotnetIsolated);
            var installationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishInstallation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int installs = 0;
            int actions = 0;
            DotnetHelpers.RunDotnetNewFunc = async _ =>
            {
                if (Interlocked.Increment(ref installs) == 1)
                {
                    CreateHivePackage(hive);
                    installationStarted.SetResult();
                    await finishInstallation.Task;
                }

                return 0;
            };
            Task Action()
            {
                Assert.True(File.Exists(Path.Combine(hive, DotnetHelpers.CustomHiveReadyFileName)));
                Interlocked.Increment(ref actions);
                return Task.CompletedTask;
            }

            Task first = DotnetHelpers.TemplateOperationAsync(Action, WorkerRuntime.DotnetIsolated);
            await installationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Task second = DotnetHelpers.TemplateOperationAsync(Action, WorkerRuntime.DotnetIsolated);
            try
            {
                Assert.False(second.IsCompleted);
                Assert.Equal(0, actions);
                Assert.Equal(1, installs);
            }
            finally
            {
                finishInstallation.TrySetResult();
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
            }

            Assert.Equal(2, actions);
            Assert.True(installs >= 2);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task TemplateOperationAsync_CustomHive_FailedInstallDoesNotPublishReadiness(int failingInstall)
        {
            string hive = EnableCustomHive(WorkerRuntime.DotnetIsolated);
            int installs = 0;
            bool actionCalled = false;
            DotnetHelpers.RunDotnetNewFunc = _ =>
            {
                CreateHivePackage(hive);
                return Task.FromResult(++installs == failingInstall ? 17 : 0);
            };
            Task Action()
            {
                actionCalled = true;
                return Task.CompletedTask;
            }

            var exception = await Assert.ThrowsAsync<CliException>(() =>
                DotnetHelpers.TemplateOperationAsync(Action, WorkerRuntime.DotnetIsolated));

            Assert.Contains("exited with code 17", exception.Message);
            Assert.False(actionCalled);
            Assert.False(File.Exists(Path.Combine(hive, DotnetHelpers.CustomHiveReadyFileName)));

            await DotnetHelpers.TemplateOperationAsync(Action, WorkerRuntime.DotnetIsolated);

            Assert.True(actionCalled);
            Assert.True(File.Exists(Path.Combine(hive, DotnetHelpers.CustomHiveReadyFileName)));
            Assert.True(installs >= failingInstall + 2);
        }

        [Fact]
        public async Task TemplateOperationAsync_CustomHive_MissingPackagesInvalidatesReadiness()
        {
            string hive = EnableCustomHive(WorkerRuntime.DotnetIsolated);
            Directory.CreateDirectory(hive);
            string readyFile = Path.Combine(hive, DotnetHelpers.CustomHiveReadyFileName);
            File.WriteAllText(readyFile, string.Empty);
            DotnetHelpers.RunDotnetNewFunc = _ =>
            {
                CreateHivePackage(hive);
                return Task.FromResult(17);
            };

            await Assert.ThrowsAsync<CliException>(() =>
                DotnetHelpers.TemplateOperationAsync(() => Task.CompletedTask, WorkerRuntime.DotnetIsolated));

            Assert.False(File.Exists(readyFile));
        }

        private string EnableCustomHive(WorkerRuntime workerRuntime)
        {
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveFlag, "1");
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveRoot, _hiveRoot);
            return Path.Combine(_hiveRoot, $"{workerRuntime.ToString().ToLowerInvariant()}-hive");
        }

        private static void CreateHivePackage(string hive)
        {
            string packages = Path.Combine(hive, "packages");
            Directory.CreateDirectory(packages);
            File.WriteAllText(Path.Combine(packages, "partial.nupkg"), string.Empty);
        }
    }
}
