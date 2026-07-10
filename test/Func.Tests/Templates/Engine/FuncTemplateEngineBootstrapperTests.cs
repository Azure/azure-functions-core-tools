// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;
using Xunit;
using Constants = Azure.Functions.Cli.Abstractions.Common.Constants;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

// Shares the FuncHome env-var collection because Create() resolves the settings
// location from FUNC_CLI_HOME; the tests redirect it to a temp directory.
[Collection("FuncHomeEnvVarTests")]
public class FuncTemplateEngineBootstrapperTests
{
    [Fact]
    public void Create_ExposesProvidedContextAsHostParams()
    {
        var context = new FuncTemplateEngineContext(Stack: "dotnet", Language: "C#", Bundle: "[4.0.0, 5.0.0)");

        WithTempHome(bootstrapper =>
        {
            using var environment = bootstrapper.Create(context);

            Assert.Equal("func", environment.Host.HostIdentifier);
            AssertHostParam(environment, FuncTemplateEngineHostParameters.Stack, "dotnet");
            AssertHostParam(environment, FuncTemplateEngineHostParameters.Language, "C#");
            AssertHostParam(environment, FuncTemplateEngineHostParameters.Bundle, "[4.0.0, 5.0.0)");
        });
    }

    [Fact]
    public void Create_OmitsUnsetContextValues()
    {
        var context = new FuncTemplateEngineContext(Stack: "node", Language: null, Bundle: "   ");

        WithTempHome(bootstrapper =>
        {
            using var environment = bootstrapper.Create(context);

            AssertHostParam(environment, FuncTemplateEngineHostParameters.Stack, "node");
            Assert.False(environment.Host.TryGetHostParamDefault(FuncTemplateEngineHostParameters.Language, out _));
            Assert.False(environment.Host.TryGetHostParamDefault(FuncTemplateEngineHostParameters.Bundle, out _));
        });
    }

    [Fact]
    public void Create_ReturnsFreshEnvironmentPerInvocation()
    {
        var context = new FuncTemplateEngineContext(Stack: "python", Language: null, Bundle: null);

        WithTempHome(bootstrapper =>
        {
            using var first = bootstrapper.Create(context);
            using var second = bootstrapper.Create(context);

            Assert.NotSame(first, second);
            Assert.NotSame(first.Host, second.Host);
        });
    }

    [Fact]
    public void Create_WithNullContext_Throws()
    {
        WithTempHome(bootstrapper =>
            Assert.Throws<ArgumentNullException>(() => bootstrapper.Create(null!)));
    }

    private static void AssertHostParam(
        Microsoft.TemplateEngine.Abstractions.IEngineEnvironmentSettings environment,
        string key,
        string expected)
    {
        Assert.True(
            environment.Host.TryGetHostParamDefault(key, out string? value),
            $"Expected host param '{key}' to be present.");
        Assert.Equal(expected, value);
    }

    private static void WithTempHome(Action<FuncTemplateEngineBootstrapper> action)
    {
        string tempHome = Path.Combine(Path.GetTempPath(), "func-boot-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable, tempHome);

            var versionProvider = Substitute.For<ICliVersionProvider>();
            versionProvider.Version.Returns("5.0.0");
            var hostFactory = new FuncTemplateEngineHostFactory(versionProvider);
            var bootstrapper = new FuncTemplateEngineBootstrapper(hostFactory);

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
