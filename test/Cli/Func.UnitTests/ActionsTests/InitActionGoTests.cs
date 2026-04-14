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
        public void ParseArgs_WorkerRuntimeNative_Parses()
        {
            var action = CreateAction();

            action.ParseArgs(new[] { "--worker-runtime", "native" });

            action.WorkerRuntime.Should().Be("native");
        }

        [Fact]
        public void ParseArgs_WorkerRuntimeNativeWithLanguageGolang_Parses()
        {
            var action = CreateAction();

            action.ParseArgs(new[] { "--worker-runtime", "native", "--language", "golang" });

            action.WorkerRuntime.Should().Be("native");
            action.Language.Should().Be("golang");
        }

        [Theory]
        [InlineData("native")]
        [InlineData("go")]
        [InlineData("golang")]
        [InlineData("Native")]
        [InlineData("Go")]
        [InlineData("Golang")]
        public void NormalizeWorkerRuntime_NativeAliases_ResolveToNative(string input)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            result.Should().Be(WorkerRuntime.Native);
        }

        [Theory]
        [InlineData("golang", "golang")]
        [InlineData("go", "golang")]
        public void NormalizeLanguage_GoAliases_ResolveToGolang(string input, string expected)
        {
            var result = WorkerRuntimeLanguageHelper.NormalizeLanguage(input);

            result.Should().Be(expected);
        }

        [Fact]
        public void GetRuntimeMoniker_Native_ReturnsNative()
        {
            var result = WorkerRuntimeLanguageHelper.GetRuntimeMoniker(WorkerRuntime.Native);

            result.Should().Be("native");
        }

        [Fact]
        public void GetDefaultTemplateLanguageFromWorker_Native_ReturnsGolang()
        {
            var result = WorkerRuntimeLanguageHelper.GetDefaultTemplateLanguageFromWorker(WorkerRuntime.Native);

            result.Should().Be(Constants.Languages.Golang);
        }

        [Fact]
        public void WorkerToSupportedLanguages_Native_ContainsGolang()
        {
            WorkerRuntimeLanguageHelper.WorkerToSupportedLanguages
                .Should().ContainKey(WorkerRuntime.Native);

            WorkerRuntimeLanguageHelper.WorkerToSupportedLanguages[WorkerRuntime.Native]
                .Should().Contain(Constants.Languages.Golang);
        }

        [Fact]
        public void ProgrammingModelHelper_Native_DefaultsToV1()
        {
            var supportedModels = ProgrammingModelHelper.GetSupportedProgrammingModels(WorkerRuntime.Native);
            supportedModels.Should().Contain(ProgrammingModel.V1);

            var resolved = ProgrammingModelHelper.ResolveProgrammingModel(null, WorkerRuntime.Native, Constants.Languages.Golang);
            resolved.Should().Be(ProgrammingModel.V1);
        }
    }
}
