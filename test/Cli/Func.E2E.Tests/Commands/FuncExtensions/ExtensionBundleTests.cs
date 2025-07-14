// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Microsoft.Azure.WebJobs.Script;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncExtensions
{
    public class ExtensionBundleTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public async Task ExtensionsInstall_BundlesConfiguredByDefault_NoActionPerformed()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(ExtensionsInstall_BundlesConfiguredByDefault_NoActionPerformed);

            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node"]);
            await FuncNewWithRetryAsync(testName, ["--template", "SendGrid", "--name", "testfunc"]);
            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(workingDir)
                            .Execute(["extensions", "install"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("No action performed");
        }

        [Fact]
        public async Task ExtensionsInstall_WithValidTrigger_NoBundleArg_PackagesRestored()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(ExtensionsInstall_WithValidTrigger_NoBundleArg_PackagesRestored);

            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node", "--no-bundle"]);
            await FuncNewWithRetryAsync(testName, ["--template", "SendGrid", "--name", "testfunc"]);
            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(workingDir)
                            .Execute(["extensions", "install"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Restoring packages for");
        }

        [Fact]
        public async Task ExtensionsInstall_WithNotValidTrigger_NoBundleArg_NoActionPerformed()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(ExtensionsInstall_WithNotValidTrigger_NoBundleArg_NoActionPerformed);

            // HttpTrigger does not require any extensions, so no action should be performed
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node", "--no-bundle"]);
            await FuncNewWithRetryAsync(testName, ["--template", "HttpTrigger", "--name", "testfunc"]);
            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(workingDir)
                            .Execute(["extensions", "install"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("No action performed");
        }

        [Fact]
        public async Task ExtensionsInstall_NoBundleArgWithExtVersion_NoActionPerformed()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(ExtensionsInstall_NoBundleArgWithExtVersion_NoActionPerformed);

            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node", "--no-bundle"]);
            await FuncNewWithRetryAsync(testName, ["--template", "SendGrid", "--name", "testfunc"]);
            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(workingDir)
                            .Execute(["extensions", "install", "-p", "-p Microsoft.Azure.WebJobs.Extensions.Storage", "-v", "3.0.8"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Restoring packages for");
        }

        [Fact]
        public async Task GetExtensionBundlePath_ReturnsBundlePath()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(GetExtensionBundlePath_ReturnsBundlePath);
            string bundlePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Common.Constants.UserCoreToolsDirectory, "Functions", ScriptConstants.ExtensionBundleDirectory);

            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node"]);
            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(workingDir)
                            .Execute(["GetExtensionBundlePath"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining(bundlePath);
        }

        [Fact]
        public async Task GetExtensionBundlePath_NoBundleArg_BundlesNotConfiigured()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(GetExtensionBundlePath_ReturnsBundlePath);
            string bundlePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Common.Constants.UserCoreToolsDirectory, "Functions", ScriptConstants.ExtensionBundleDirectory);

            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node", "--no-bundle"]);
            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(workingDir)
                            .Execute(["GetExtensionBundlePath"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Extension bundle not configured.");
        }
    }
}
