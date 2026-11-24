// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Templating;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Templating;

public sealed class TemplateArgumentBinderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"func-template-binder-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Bind_MapsTemplateAuthoredAliasesToNativeParameters()
    {
        FuncTemplateInfo template = await GetTemplateAsync();

        IReadOnlyDictionary<string, string> values = TemplateArgumentBinder.Bind(
            template, ["-tfm", "net11.0", "--no-http=false", "--no-telemetry"], [], out IReadOnlyList<string> unrecognized);

        unrecognized.Should().BeEmpty();
        values.Should().Equal(new Dictionary<string, string>
        {
            ["TargetFramework"] = "net11.0",
            ["NoHttp"] = "false",
            ["NoTelemetry"] = "true",
        });
    }

    [Theory]
    [InlineData("--no-http=garbage", "--no-http")]
    [InlineData("--no-telemetry:garbage", "--no-telemetry")]
    public async Task Bind_InvalidInlineBooleanValue_NamesTheOptionAndValue(string token, string option)
    {
        FuncTemplateInfo template = await GetTemplateAsync();

        Action bind = () => TemplateArgumentBinder.Bind(template, [token], [], out _);

        bind.Should().Throw<InvalidOperationException>()
            .WithMessage($"Invalid value 'garbage' for option '{option}'. Expected 'true' or 'false'.");
    }

    [Fact]
    public async Task Bind_LeavesUnconsumedTokensToTheCaller()
    {
        FuncTemplateInfo template = await GetTemplateAsync();

        IReadOnlyDictionary<string, string> values = TemplateArgumentBinder.Bind(
            template, ["--target-framework", "net11.0", "MyApp"], [], out IReadOnlyList<string> unrecognized);

        values.Should().BeEmpty();
        unrecognized.Should().Equal("MyApp");
    }

    private async Task<FuncTemplateInfo> GetTemplateAsync()
    {
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteInitProjectPackage(_root), hive);
        using Templater templater = new(
            new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)),
            hive,
            new FuncTemplateComponentsProvider([], [], Substitute.For<IInteractionService>()).Get());
        TemplateResolutionResult result = await templater.ListProjectTemplatesAsync("python", CancellationToken.None);
        return result.Templates.Single(template => template.Identity == TemplateFixtures.InitProjectTemplateIdentity);
    }
}
