// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Tests.Commands;

public class PublishCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void Publish_IsHidden()
    {
        var command = new PublishCommand(_interaction);

        command.Hidden.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_PrintsAzCliSampleAndV4Redirect_AndReturnsNonZero()
    {
        var command = new PublishCommand(_interaction);
        var parseResult = command.Parse([]);

        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        _interaction.AllOutput.Should().Contain("not supported in v5");
        _interaction.AllOutput.Should().Contain("az functionapp deployment");
        _interaction.AllOutput.Should().Contain("functions-run-local");
    }

    [Theory]
    [InlineData("--publish-local-settings")]
    [InlineData("-i")]
    [InlineData("--publish-settings-only")]
    [InlineData("-o")]
    [InlineData("--overwrite-settings")]
    [InlineData("-y")]
    public async Task ExecuteAsync_WithSettingsFlag_PointsToAzAppSettings(string flag)
    {
        var command = new PublishCommand(_interaction);
        var parseResult = command.Parse(["myapp", flag]);

        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        _interaction.AllOutput.Should().Contain("az functionapp config appsettings set");
    }

    [Theory]
    [InlineData("--publish-settings-only")]
    [InlineData("-o")]
    public async Task ExecuteAsync_WithSettingsOnlyFlag_OmitsDeploymentGuidance(string flag)
    {
        var command = new PublishCommand(_interaction);
        var parseResult = command.Parse(["myapp", flag]);

        await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        _interaction.AllOutput.Should().NotContain("az functionapp deployment");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutSettingsFlag_OmitsAppSettingsGuidance()
    {
        var command = new PublishCommand(_interaction);
        var parseResult = command.Parse(["myapp"]);

        await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        _interaction.AllOutput.Should().NotContain("appsettings");
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresExtraArguments()
    {
        var command = new PublishCommand(_interaction);
        var parseResult = command.Parse(["myapp", "--slot", "staging"]);

        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        _interaction.AllOutput.Should().Contain("not supported in v5");
    }
}
