// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Microsoft.Azure.WebJobs.Script;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncExtensions
{
    public class ExtensionBundleTestsBase(ITestOutputHelper log) : BaseE2ETests(log)
    {
        public async Task ExtensionsInstallBundlesConfiguredByDefault(string worker, string templateName, string testName, params string[] additionalInitArgs)
        {
            var initArgs = new[] { ".", "--worker-runtime", worker }.Concat(additionalInitArgs ?? Array.Empty<string>()).ToArray();

            await FuncInitWithRetryAsync(testName, initArgs);
            await FuncNewWithRetryAsync(testName, ["--template", templateName, "--name", "testfunc"]);

            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(WorkingDirectory)
                            .Execute(["extensions", "install"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("No action performed. Extension bundle is configured");
        }

        public async Task ExtensionsInstallNoBundleArgWithExtVersion(string worker, string templateName, string testName, params string[] additionalInitArgs)
        {
            var initArgs = new[] { ".", "--worker-runtime", worker, "--no-bundle" }.Concat(additionalInitArgs ?? Array.Empty<string>()).ToArray();

            await FuncInitWithRetryAsync(testName, initArgs);
            await FuncNewWithRetryAsync(testName, ["--template", templateName, "--name", "testfunc"]);

            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(WorkingDirectory)
                            .Execute(["extensions", "install", "-p", "Microsoft.Azure.WebJobs.Extensions.Storage", "-v", "5.3.0"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Restoring packages for");
            result.Should().HaveStdOutContaining("Microsoft.Azure.WebJobs.Extensions.Storage");
        }

        public async Task ExtensionsInstallNoBundleArgWithNotValidTrigger(string worker, string templateName, string testName, params string[] additionalInitArgs)
        {
            var initArgs = new[] { ".", "--worker-runtime", worker, "--no-bundle" }.Concat(additionalInitArgs ?? Array.Empty<string>()).ToArray();

            await FuncInitWithRetryAsync(testName, initArgs);
            await FuncNewWithRetryAsync(testName, ["--template", templateName, "--name", "testfunc"]);

            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(WorkingDirectory)
                            .Execute(["extensions", "install"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("No action performed because no functions in your app require extensions");
        }

        public async Task GetExtensionBundlePathReturnsBundlePath(string worker, string testName, params string[] additionalInitArgs)
        {
            var initArgs = new[] { ".", "--worker-runtime", worker }.Concat(additionalInitArgs ?? Array.Empty<string>()).ToArray();
            string bundlePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Common.Constants.UserCoreToolsDirectory, "Functions", ScriptConstants.ExtensionBundleDirectory);

            await FuncInitWithRetryAsync(testName, initArgs);

            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(WorkingDirectory)
                            .Execute(["GetExtensionBundlePath"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining(bundlePath);
        }

        public async Task GetExtensionBundlePathNoBundleArgBundlesNotConfiigured(string worker, string testName, params string[] additionalInitArgs)
        {
            var initArgs = new[] { ".", "--worker-runtime", worker, "--no-bundle" }.Concat(additionalInitArgs ?? Array.Empty<string>()).ToArray();

            await FuncInitWithRetryAsync(testName, initArgs);

            var result = new FuncRootCommand(FuncPath, testName, Log)
                            .WithWorkingDirectory(WorkingDirectory)
                            .Execute(["GetExtensionBundlePath"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Extension bundle not configured.");
        }
    }
}
