// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Tests.Console;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupRendererTests
{
    [Fact]
    public void Warning_JsonWithNarrowNonInteractiveSpectreConsole_WritesOnePhysicalLine()
    {
        using var stdout = new BufferedConsole(interactive: false, ansi: false, noColor: true);
        using var stderr = new BufferedConsole(interactive: false, ansi: false, noColor: true);
        stdout.Profile.Width = 80;
        var interaction = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        var renderer = new SetupRenderer(interaction, SetupOutputMode.Json);
        string message = $"[red]{new string('x', 200)}[/] \"quoted\"\r\nnext\tline \u001b[31m";

        renderer.Warning(message);

        using StringReader reader = new(stdout.Output);
        string? line = reader.ReadLine();
        line.Should().NotBeNull();
        line!.Length.Should().BeGreaterThan(80);
        reader.ReadLine().Should().BeNull();
        stdout.Output.Should().Be(line + Environment.NewLine);
        using var document = JsonDocument.Parse(line);
        document.RootElement.GetProperty("type").GetString().Should().Be("setup.warning");
        document.RootElement.GetProperty("message").GetString().Should().Be(message);
        document.RootElement.GetProperty("timestamp").GetDateTimeOffset().Should().NotBe(default);
        stderr.Output.Should().BeEmpty();
        stdout.Reads.Should().Be(0);
        stderr.Reads.Should().Be(0);
    }
}