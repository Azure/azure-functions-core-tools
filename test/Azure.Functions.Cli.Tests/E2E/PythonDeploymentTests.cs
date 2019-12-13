using System;
using Xunit;
using Xunit.Abstractions;
using Azure.Functions.Cli.Tests.E2E.Helpers;

namespace Azure.Functions.Cli.Tests.E2E
{
    
    [Collection("PythonDeploymentTestCollection")]
    public class PythonDeploymentTests : BaseE2ETest
    {
        private readonly string _v2LinuxDedicatedPython36;

        public PythonDeploymentTests(ITestOutputHelper output) : base(output)
        {
            _v2LinuxDedicatedPython36 = Environment.GetEnvironmentVariable("V2_LINUX_DED_PYTHON36");
        }

        [SkippableFact]
        public async void V2LinuxDedicatedPython36Default()
        {
            TestConditions.SkipIfEnableDeploymentTestsNotDefined();
            string appName = _v2LinuxDedicatedPython36;
            string triggerName = Guid.NewGuid().ToString();
            await CliTester.Run(new RunConfiguration[] {
                DeploymentTestHelper.GenerateInitTask(triggerName),
                DeploymentTestHelper.GeneratePublishTask(appName, string.Empty, new [] { "Remote build succeeded!" }),
                DeploymentTestHelper.GenerateRequestTask(appName, triggerName)
            }, _output);
        }

        [SkippableFact]
        public async void V2LinuxDedicatedPython36RemoteBuild()
        {
            TestConditions.SkipIfEnableDeploymentTestsNotDefined();
            string appName = _v2LinuxDedicatedPython36;
            string triggerName = Guid.NewGuid().ToString();
            await CliTester.Run(new RunConfiguration[] {
                DeploymentTestHelper.GenerateInitTask(triggerName),
                DeploymentTestHelper.GeneratePublishTask(appName, "--build remote", new [] { "Remote build succeeded!" }),
                DeploymentTestHelper.GenerateRequestTask(appName, triggerName)
            }, _output);
        }

        [SkippableFact]
        public async void V2LinuxDedicatedPython36LocalBuild()
        {
            TestConditions.SkipIfEnableDeploymentTestsNotDefined();
            string appName = _v2LinuxDedicatedPython36;
            string triggerName = Guid.NewGuid().ToString();
            await CliTester.Run(new RunConfiguration[] {
                DeploymentTestHelper.GenerateInitTask(triggerName),
                DeploymentTestHelper.GeneratePublishTask(appName, "--build local", new [] { "Deployment completed successfully." }),
                DeploymentTestHelper.GenerateRequestTask(appName, triggerName)
            }, _output);
        }
    }
}
