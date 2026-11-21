// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Tests.Commands;

/// <summary>
/// Behavioral tests for the shared <c>[path]</c> argument used by every
/// project-aware command. Exercised through a minimal <see cref="FuncCliCommand"/>
/// host so the parser wiring matches what real commands see.
/// </summary>
public class PathArgumentTests
{
    [Fact]
    public void ExplicitPath_ReturnsExplicitLocationWithThatDirectory()
    {
        var cmd = new TestableCommand();
        var parseResult = cmd.Parse(["/tmp/some-project"]);

        var workingDirectory = parseResult.GetValue(cmd.PathArgument!);

        workingDirectory.Should().NotBeNull();
        workingDirectory!.WasExplicit.Should().BeTrue();
        workingDirectory.OriginalPath.Should().Be("/tmp/some-project");
        workingDirectory.Info.Name.Should().EndWith("some-project");
    }

    [Fact]
    public void NoPath_FallsBackToCurrentDirectoryAndIsNotExplicit()
    {
        var cmd = new TestableCommand();
        var parseResult = cmd.Parse([]);

        var workingDirectory = parseResult.GetValue(cmd.PathArgument!);

        workingDirectory.Should().NotBeNull();
        workingDirectory!.WasExplicit.Should().BeFalse();
        workingDirectory.OriginalPath.Should().BeNull();
        workingDirectory.Info.FullName.Should().Be(Directory.GetCurrentDirectory());
    }

    [Fact]
    public void DashPrefix_SurfacesAsParseError()
    {
        var cmd = new TestableCommand();
        var parseResult = cmd.Parse(["--bogus"]);

        parseResult.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(new[] { "myproj", "--framework", "net8.0" }, "myproj", new[] { "--framework", "net8.0" })]
    [InlineData(new[] { "--framework", "net8.0", "--no-http" }, null, new[] { "--framework", "net8.0", "--no-http" })]
    [InlineData(new[] { "--", "--framework", "x" }, null, new[] { "--", "--framework", "x" })]
    [InlineData(new[] { "myproj", "--", "x" }, "myproj", new[] { "--", "x" })]
    [InlineData(new[] { "myproj" }, "myproj", new string[0])]
    public void CaptureTemplateArguments_KeepsUndeclaredTokensInOrder(string[] args, string? expectedPath, string[] expectedTokens)
    {
        var cmd = new TestableCommand(captureTemplateArguments: true);

        var parseResult = cmd.Parse(args);

        parseResult.Errors.Should().BeEmpty();
        parseResult.GetValue(cmd.PathArgument!)!.OriginalPath.Should().Be(expectedPath);
        cmd.TemplateArgumentsArgument!.GetTokens(parseResult).Should().Equal(expectedTokens);
    }

    private sealed class TestableCommand : FuncCliCommand
    {
        public TestableCommand(bool captureTemplateArguments = false) : base("testable", "Testable command for [path] parsing.")
        {
            AddPathArgument(captureTemplateArguments);
        }

        protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}
