// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using AwesomeAssertions;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ConfigurationProfiles;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    [Collection("NativeWorkerRuntimeTests")]
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
        public void GetCurrentWorkerRuntimeLanguage_NativeWithGoMod_ResolvesToGo(string settingValue)
        {
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsWorkerRuntime, settingValue },
            });

            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Is<string>(p => p.EndsWith("go.mod"))).Returns(true);

            var previousEnv = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                using (FileSystemHelpers.Override(fileSystem))
                {
                    var resolved = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(secretsManager);

                    resolved.Should().Be(WorkerRuntime.Go);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousEnv);
            }
        }

        [Fact]
        public void GetCurrentWorkerRuntimeLanguage_NativeWithoutGoMod_Throws()
        {
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsWorkerRuntime, "native" },
            });

            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(false);

            var previousEnv = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                using (FileSystemHelpers.Override(fileSystem))
                {
                    Action act = () => WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(secretsManager);

                    act.Should().Throw<CliException>().WithMessage("*native*");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousEnv);
            }
        }

        [Fact]
        public void GetCurrentWorkerRuntimeLanguage_NonNativeSetting_NormalizesAndReturns()
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
                var resolved = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(secretsManager);

                resolved.Should().Be(WorkerRuntime.Node);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousEnv);
            }
        }

        [Theory]
        [InlineData("--go")]
        [InlineData("--golang")]
        public void GlobalCoreToolsSettings_Init_GoShortcutFlag_SetsCurrentWorkerRuntimeToGo(string flag)
        {
            var previousRuntime = GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone;
            var previousEnv = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);
            try
            {
                GlobalCoreToolsSettings.Init(secretsManager: null, args: new[] { flag });

                GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone.Should().Be(WorkerRuntime.Go);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousEnv);
                GlobalCoreToolsSettings.CurrentWorkerRuntime = previousRuntime;
            }
        }

        [Theory]
        [InlineData("go")]
        [InlineData("Go")]
        [InlineData("GO")]
        public void GetCurrentWorkerRuntimeLanguage_GoPreviewEnvVar_ResolvesToGo(string envValue)
        {
            // Env var takes precedence over local.settings.json and the go.mod fallback.
            // Even with no project markers and no FUNCTIONS_WORKER_RUNTIME, the explicit
            // preview opt-in is enough to select the Go runtime.
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var previousFlag = Environment.GetEnvironmentVariable(Constants.FunctionsCliNativeLanguage);
            var previousRuntime = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliNativeLanguage, envValue);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                var resolved = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(secretsManager);

                resolved.Should().Be(WorkerRuntime.Go);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliNativeLanguage, previousFlag);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousRuntime);
            }
        }

        [Theory]
        [InlineData("go")]
        [InlineData("Go")]
        [InlineData("GO")]
        public void GetCurrentWorkerRuntimeLanguage_GoPreviewSetting_ResolvesToGo(string settingValue)
        {
            // local.settings.json wins when the env var isn't set, without any go.mod scan.
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsCliNativeLanguage, settingValue },
                { Constants.FunctionsWorkerRuntime, "native" },
            });

            var previousFlag = Environment.GetEnvironmentVariable(Constants.FunctionsCliNativeLanguage);
            var previousRuntime = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliNativeLanguage, null);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                var resolved = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(secretsManager);

                resolved.Should().Be(WorkerRuntime.Go);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliNativeLanguage, previousFlag);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousRuntime);
            }
        }

        [Theory]
        [InlineData("python")]
        [InlineData("rust")]
        [InlineData("")]
        public void GetCurrentWorkerRuntimeLanguage_GoPreviewFalsy_FallsThroughToLegacyResolution(string flagValue)
        {
            // Non-"go" flag values must not short-circuit; the legacy native+go.mod path should still run.
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsCliNativeLanguage, flagValue },
                { Constants.FunctionsWorkerRuntime, "native" },
            });

            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Is<string>(p => p.EndsWith("go.mod"))).Returns(true);

            var previousFlag = Environment.GetEnvironmentVariable(Constants.FunctionsCliNativeLanguage);
            var previousRuntime = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliNativeLanguage, null);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                using (FileSystemHelpers.Override(fileSystem))
                {
                    var resolved = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(secretsManager);

                    resolved.Should().Be(WorkerRuntime.Go);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliNativeLanguage, previousFlag);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousRuntime);
            }
        }

        [Fact]
        public void GetCurrentWorkerRuntimeLanguage_GoPreviewEnvVar_BeatsSecretsManagerThrow()
        {
            // SecretsManager throwing (e.g. command run from outside a project root) must not
            // mask an explicit env-var opt-in.
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(_ => throw new CliException("no project"));

            var previousFlag = Environment.GetEnvironmentVariable(Constants.FunctionsCliNativeLanguage);
            var previousRuntime = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliNativeLanguage, "go");
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                var resolved = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(secretsManager);

                resolved.Should().Be(WorkerRuntime.Go);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliNativeLanguage, previousFlag);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousRuntime);
            }
        }
    }
}
