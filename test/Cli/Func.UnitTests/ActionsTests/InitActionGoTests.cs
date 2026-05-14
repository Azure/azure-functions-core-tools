// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
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
        public void ResolveNativeWorkerRuntime_WithGoMod_ResolvesToGo(string settingValue)
        {
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsWorkerRuntime, settingValue },
            });

            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Is<string>(p => p.EndsWith("go.mod"))).Returns(true);

            var previousEnv = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            var previous = GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone;
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                using (FileSystemHelpers.Override(fileSystem))
                {
                    WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime(secretsManager);

                    GlobalCoreToolsSettings.CurrentWorkerRuntime.Should().Be(WorkerRuntime.Go);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousEnv);
                GlobalCoreToolsSettings.CurrentWorkerRuntime = previous;
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

            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(false);

            var previousEnv = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                using (FileSystemHelpers.Override(fileSystem))
                {
                    Action act = () => WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime(secretsManager);

                    act.Should().Throw<CliException>().WithMessage("*native*");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousEnv);
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

        [Theory]
        [InlineData("true")]
        [InlineData("True")]
        [InlineData("TRUE")]
        [InlineData("1")]
        public void ResolveNativeWorkerRuntime_GoPreviewEnvVar_ResolvesToGo(string envValue)
        {
            // Env var takes precedence over local.settings.json and the go.mod fallback.
            // Even with no project markers and no FUNCTIONS_WORKER_RUNTIME, the explicit
            // preview opt-in is enough to select the Go runtime.
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var previousFlag = Environment.GetEnvironmentVariable(Constants.FunctionsCliGoPreview);
            var previousRuntime = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            var previous = GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone;
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliGoPreview, envValue);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime(secretsManager);

                GlobalCoreToolsSettings.CurrentWorkerRuntime.Should().Be(WorkerRuntime.Go);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliGoPreview, previousFlag);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousRuntime);
                GlobalCoreToolsSettings.CurrentWorkerRuntime = previous;
            }
        }

        [Theory]
        [InlineData("true")]
        [InlineData("True")]
        [InlineData("1")]
        public void ResolveNativeWorkerRuntime_GoPreviewSetting_ResolvesToGo(string settingValue)
        {
            // local.settings.json wins when the env var isn't set, without any go.mod scan.
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsCliGoPreview, settingValue },
                { Constants.FunctionsWorkerRuntime, "native" },
            });

            var previousFlag = Environment.GetEnvironmentVariable(Constants.FunctionsCliGoPreview);
            var previousRuntime = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            var previous = GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone;
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliGoPreview, null);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime(secretsManager);

                GlobalCoreToolsSettings.CurrentWorkerRuntime.Should().Be(WorkerRuntime.Go);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliGoPreview, previousFlag);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousRuntime);
                GlobalCoreToolsSettings.CurrentWorkerRuntime = previous;
            }
        }

        [Theory]
        [InlineData("false")]
        [InlineData("0")]
        [InlineData("yes")]
        [InlineData("")]
        public void ResolveNativeWorkerRuntime_GoPreviewFalsy_FallsThroughToLegacyResolution(string flagValue)
        {
            // Falsy flag values must not short-circuit; the legacy native+go.mod path should still run.
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { Constants.FunctionsCliGoPreview, flagValue },
                { Constants.FunctionsWorkerRuntime, "native" },
            });

            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Is<string>(p => p.EndsWith("go.mod"))).Returns(true);

            var previousFlag = Environment.GetEnvironmentVariable(Constants.FunctionsCliGoPreview);
            var previousRuntime = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            var previous = GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone;
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliGoPreview, null);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                using (FileSystemHelpers.Override(fileSystem))
                {
                    WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime(secretsManager);

                    GlobalCoreToolsSettings.CurrentWorkerRuntime.Should().Be(WorkerRuntime.Go);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliGoPreview, previousFlag);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousRuntime);
                GlobalCoreToolsSettings.CurrentWorkerRuntime = previous;
            }
        }

        [Fact]
        public void ResolveNativeWorkerRuntime_GoPreviewEnvVar_BeatsSecretsManagerThrow()
        {
            // SecretsManager throwing (e.g. command run from outside a project root) must not
            // mask an explicit env-var opt-in.
            var secretsManager = Substitute.For<ISecretsManager>();
            secretsManager.GetSecrets(Arg.Any<bool>()).Returns(_ => throw new CliException("no project"));

            var previousFlag = Environment.GetEnvironmentVariable(Constants.FunctionsCliGoPreview);
            var previousRuntime = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime);
            var previous = GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone;
            try
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliGoPreview, "true");
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, null);

                WorkerRuntimeLanguageHelper.ResolveNativeWorkerRuntime(secretsManager);

                GlobalCoreToolsSettings.CurrentWorkerRuntime.Should().Be(WorkerRuntime.Go);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Constants.FunctionsCliGoPreview, previousFlag);
                Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntime, previousRuntime);
                GlobalCoreToolsSettings.CurrentWorkerRuntime = previous;
            }
        }
    }
}
