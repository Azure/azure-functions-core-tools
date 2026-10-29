// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates.Selection;

namespace Azure.Functions.Cli.Tests.Templates.Selection;

public sealed class TemplateArgumentTransportTests
{
    [Theory]
    [InlineData("same")]
    [InlineData("")]
    public void Capture_EqualHostAndTemplateValues_RetainsOnlyTheActualTemplateTokens(string value)
    {
        var path = new PathArgument();
        var name = new Option<string>("--name", "-n");
        var parameter = new Option<string>("--value");
        var command = new Command("probe") { path, name, parameter };
        string target = Path.GetTempPath();
        var parsed = command.Parse([target, "-n", value, "--value", value]);

        var captured = TemplateArgumentTransport.Capture(parsed, path, [name]);

        captured.Arguments.Should().Equal("--value", value);
        captured.Reserved.Should().Contain("--name").And.Contain("-n").And.NotContain("--value");
    }

    [Fact]
    public void Capture_RepeatedTemplateOptions_PreservesBothOccurrencesAndExplicitDefaults()
    {
        var path = new PathArgument();
        var name = new Option<string>("--name");
        var parameter = new Option<string>("--value");
        var command = new Command("probe") { path, name, parameter };
        var parsed = command.Parse([Path.GetTempPath(), "--value", "default", "--value", "default"]);

        var captured = TemplateArgumentTransport.Capture(parsed, path, [name]);

        captured.Arguments.Should().Equal("--value", "default", "--value", "default");
    }
}
