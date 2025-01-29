using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Microsoft.Azure.WebJobs.Script;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.ExtensionsTests
{
    public class ExtensionBundleTests : BaseE2ETest
    {
        public ExtensionBundleTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public Task BundleConfiguredByDefault_no_action()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] {
                    "init . --worker-runtime node",
                    "new --template SendGrid --name testfunc",
                    "extensions install"
                },
                OutputContains = new[]
                {
                    "No action performed"
                },
                ExitInError = true,
                CommandTimeout = TimeSpan.FromMinutes(300)
            }, _output);
        }

        [Fact]
        public Task BundleConfiguredByDefault_findsBundlePath()
        {

            string bundlePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.UserCoreToolsDirectory, "Functions", ScriptConstants.ExtensionBundleDirectory);
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] {
                    "init . --worker-runtime node",
                    "GetExtensionBundlePath"
                },
                OutputContains = new[]
                {
                    bundlePath
                },
                CommandTimeout = TimeSpan.FromMinutes(300)
            }, _output);
        }

        [Fact]
        public Task BundleNotConfiguredByDefault_showsErrorMessage()
        {

            string bundlePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.UserCoreToolsDirectory, "Functions", ScriptConstants.ExtensionBundleDirectory);
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] {
                    "init . --worker-runtime node --no-bundle",
                    "GetExtensionBundlePath"
                },
                OutputContains = new[]
                {
                    "Extension bundle not configured."
                },
                CommandTimeout = TimeSpan.FromMinutes(300)
            }, _output);
        }
    }
}
