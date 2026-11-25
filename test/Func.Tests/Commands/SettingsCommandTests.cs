// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Tests.Commands;

public class SettingsCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void Settings_IsHidden()
    {
        var command = new SettingsCommand(_interaction);

        command.Hidden.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithV4Action_NamesActionAndPointsToLocalSettings()
    {
        var command = new SettingsCommand(_interaction);
        var parseResult = command.Parse(["add", "MySetting", "value", "--connectionString"]);

        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        _interaction.AllOutput.Should().Contain("'func settings add' is not supported in v5");
        _interaction.AllOutput.Should().Contain("Edit local.settings.json directly");
    }

    [Theory]
    [InlineData("encrypt")]
    [InlineData("decrypt")]
    public async Task ExecuteAsync_WithEncryptionAction_PointsToV4Decrypt(string action)
    {
        var command = new SettingsCommand(_interaction);
        var parseResult = command.Parse([action]);

        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        _interaction.AllOutput.Should().Contain($"'func settings {action}' is not supported in v5");
        _interaction.AllOutput.Should().Contain("func settings decrypt");
    }

    [Fact]
    public async Task ExecuteAsync_WithOptionValueBeforeAction_NamesAction()
    {
        var command = new SettingsCommand(_interaction);
        var parseResult = command.Parse(["--file", "custom.json", "encrypt"]);

        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        _interaction.AllOutput.Should().Contain("'func settings encrypt' is not supported in v5");
        _interaction.AllOutput.Should().NotContain("custom.json");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutAction_ReturnsNonZero()
    {
        var command = new SettingsCommand(_interaction);
        var parseResult = command.Parse([]);

        var exitCode = await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        _interaction.AllOutput.Should().Contain("'func settings' is not supported in v5");
    }
}
