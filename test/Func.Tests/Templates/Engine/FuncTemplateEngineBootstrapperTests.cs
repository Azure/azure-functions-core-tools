// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;
using Xunit;
using Constants = Azure.Functions.Cli.Abstractions.Common.Constants;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

[Collection("FuncHomeEnvVarTests")]
public class FuncTemplateEngineBootstrapperTests
{
    [Fact]
    public void Create_ExposesProvidedContextAsHostParameters()
    {
        FuncTemplateEngineContext context = new(
            Stack: "dotnet",
            Language: "C#",
            BundleId: "Microsoft.Azure.Functions.ExtensionBundle",
            BundleVersion: "4.26.2");

        WithTempHome(bootstrapper =>
        {
            using IEngineEnvironmentSettings environment = bootstrapper.Create(context);

            environment.Host.HostIdentifier.Should().Be(FuncTemplateEngineHost.Identifier);
            AssertHostParameter(environment, FuncTemplateEngineHostParameters.Stack, "dotnet");
            AssertHostParameter(environment, FuncTemplateEngineHostParameters.Language, "C#");
            AssertHostParameter(
                environment,
                FuncTemplateEngineHostParameters.BundleId,
                "Microsoft.Azure.Functions.ExtensionBundle");
            AssertHostParameter(environment, FuncTemplateEngineHostParameters.BundleVersion, "4.26.2");
        });
    }

    [Fact]
    public void Create_OmitsUnsetContextValues()
    {
        FuncTemplateEngineContext context = new(
            Stack: "node",
            Language: null,
            BundleId: " ",
            BundleVersion: null);

        WithTempHome(bootstrapper =>
        {
            using IEngineEnvironmentSettings environment = bootstrapper.Create(context);

            AssertHostParameter(environment, FuncTemplateEngineHostParameters.Stack, "node");
            environment.Host.TryGetHostParamDefault(FuncTemplateEngineHostParameters.Language, out _).Should().BeFalse();
            environment.Host.TryGetHostParamDefault(FuncTemplateEngineHostParameters.BundleId, out _).Should().BeFalse();
            environment.Host.TryGetHostParamDefault(FuncTemplateEngineHostParameters.BundleVersion, out _).Should().BeFalse();
        });
    }

    [Fact]
    public void Create_ReturnsFreshEnvironmentPerInvocation()
    {
        FuncTemplateEngineContext context = new("python", null, null, null);

        WithTempHome(bootstrapper =>
        {
            using IEngineEnvironmentSettings first = bootstrapper.Create(context);
            using IEngineEnvironmentSettings second = bootstrapper.Create(context);

            second.Should().NotBeSameAs(first);
            second.Host.Should().NotBeSameAs(first.Host);
        });
    }

    [Fact]
    public void Create_WithNullContext_Throws()
    {
        WithTempHome(bootstrapper =>
        {
            Action act = () => bootstrapper.Create(null!);

            act.Should().ThrowExactly<ArgumentNullException>();
        });
    }

    private static void AssertHostParameter(IEngineEnvironmentSettings environment, string key, string expected)
    {
        environment.Host.TryGetHostParamDefault(key, out string? value).Should().BeTrue();
        value.Should().Be(expected);
    }

    private static void WithTempHome(Action<FuncTemplateEngineBootstrapper> action)
    {
        string tempHome = Path.Combine(Path.GetTempPath(), "func-template-bootstrapper-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable, tempHome);

            ICliVersionProvider versionProvider = Substitute.For<ICliVersionProvider>();
            versionProvider.Version.Returns("5.0.0");
            FuncTemplateEngineHostFactory hostFactory = new(versionProvider);
            FuncTemplateEngineBootstrapper bootstrapper = new(hostFactory);

            action(bootstrapper);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable, previous);
            if (Directory.Exists(tempHome))
            {
                Directory.Delete(tempHome, recursive: true);
            }
        }
    }
}
