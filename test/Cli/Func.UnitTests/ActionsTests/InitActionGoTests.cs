// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ConfigurationProfiles;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class InitActionGoTests
    {
        private static InitAction CreateAction()
        {
            var templatesManager = Substitute.For<ITemplatesManager>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var configProfiles = new List<IConfigurationProfile>();
            return new InitAction(templatesManager, secretsManager, configProfiles);
        }

        [Fact]
        public void ParseArgs_SkipGoModTidy_DefaultsToFalse()
        {
            var action = CreateAction();

            action.ParseArgs(Array.Empty<string>());

            action.SkipGoModTidy.Should().BeFalse();
        }

        [Fact]
        public void ParseArgs_WithSkipGoModTidyFlag_SetsTrue()
        {
            var action = CreateAction();

            action.ParseArgs(new[] { "--skip-go-mod-tidy" });

            action.SkipGoModTidy.Should().BeTrue();
        }

        [Fact]
        public void ParseArgs_WorkerRuntimeGo_Parses()
        {
            var action = CreateAction();

            action.ParseArgs(new[] { "--worker-runtime", "go" });

            action.WorkerRuntime.Should().Be("go");
        }

        [Theory]
        [InlineData("go")]
        [InlineData("golang")]
        [InlineData("Go")]
        [InlineData("Golang")]
        public void NormalizeWorkerRuntime_GoAliases_ResolveToGo(string input)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            result.Should().Be(WorkerRuntime.Go);
        }

        [Fact]
        public void GetRuntimeMoniker_Go_ReturnsGo()
        {
            var result = WorkerRuntimeLanguageHelper.GetRuntimeMoniker(WorkerRuntime.Go);

            result.Should().Be("go");
        }

        [Fact]
        public void ProgrammingModelHelper_Go_DefaultsToV1()
        {
            var supportedModels = ProgrammingModelHelper.GetSupportedProgrammingModels(WorkerRuntime.Go);
            supportedModels.Should().Contain(ProgrammingModel.V1);

            var resolved = ProgrammingModelHelper.ResolveProgrammingModel(null, WorkerRuntime.Go, string.Empty);
            resolved.Should().Be(ProgrammingModel.V1);
        }

        [Theory]
        [InlineData("native")]
        [InlineData("Native")]
        [InlineData("NATIVE")]
        public void ResolveNativeWorkerRuntime_WithGoMod_ResolvesToGo(string settingValue)
        {
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsWorkerRuntime, settingValue },
            });

            var previousEnv = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            var previousDir = Environment.CurrentDirectory;
            var tempDir = Path.Combine(Path.GetTempPath(), "func-test-native-" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "go.mod"), "module test");
                Environment.CurrentDirectory = tempDir;
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime(secretsManager);

                GlobalCoreToolsSettings.CurrentWorkerRuntime.Should().Be(WorkerRuntime.Go);
            }
            finally
            {
                Environment.CurrentDirectory = previousDir;
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousEnv);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void ResolveNativeWorkerRuntime_WithoutGoMod_Throws()
        {
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsWorkerRuntime, "native" },
            });

            var previousEnv = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            var previousDir = Environment.CurrentDirectory;
            var tempDir = Path.Combine(Path.GetTempPath(), "func-test-native-" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                Environment.CurrentDirectory = tempDir;
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                Action act = () => WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime(secretsManager);

                act.Should().Throw<CliException>().WithMessage("*native*");
            }
            finally
            {
                Environment.CurrentDirectory = previousDir;
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousEnv);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void ResolveNativeWorkerRuntime_NonNativeSetting_IsNoOp()
        {
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsWorkerRuntime, "node" },
            });

            var previousEnv = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);
            try
            {
                // Should not throw, should not change the runtime
                WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime(secretsManager);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousEnv);
            }
        }
    }
}
