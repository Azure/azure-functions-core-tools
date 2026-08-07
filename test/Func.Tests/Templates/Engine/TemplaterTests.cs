// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class TemplaterTests
{
    [Fact]
    public void Constructor_UsesBootstrappedEnvironment()
    {
        using IEngineEnvironmentSettings environment = CreateEnvironment();

        Templater templater = new(environment);

        templater.Settings.Should().BeSameAs(environment);
        templater.Creator.Should().NotBeNull();
        templater.Settings.Components.OfType<IGenerator>().Should().NotBeEmpty();
        FuncTemplateComponents.AllComponents.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithNullEnvironment_Throws()
    {
        Action act = () => new Templater(null!);

        act.Should().ThrowExactly<ArgumentNullException>();
    }

    private static IEngineEnvironmentSettings CreateEnvironment()
    {
        FuncTemplateEngineHost host = new("5.0.0", defaults: null);
        return new EngineEnvironmentSettings(
            host,
            settingsLocation: Path.Combine(Path.GetTempPath(), "func-templater-" + Guid.NewGuid().ToString("N")));
    }
}
