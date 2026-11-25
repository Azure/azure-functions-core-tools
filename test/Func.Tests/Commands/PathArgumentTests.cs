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

    [Theory]
    [InlineData(new[] { "--bogus" }, new[] { "--bogus" }, null)]
    [InlineData(new[] { "MyApp", "--bogus" }, new[] { "--bogus" }, "MyApp")]
    [InlineData(new[] { "--bogus", "MyApp" }, new[] { "--bogus", "MyApp" }, null)]
    public void DashPrefix_IsLeftUnmatchedAndSurfacesAsUnrecognized(
        string[] args,
        string[] expectedUnmatched,
        string? expectedPath)
    {
        var cmd = new TestableCommand();
        var parseResult = cmd.Parse(args);

        parseResult.UnmatchedTokens.Should().Equal(expectedUnmatched);
        parseResult.Errors.Should().Contain(e => e.Message.Contains("'--bogus'"));
        parseResult.GetValue(cmd.PathArgument!)!.OriginalPath.Should().Be(expectedPath);
    }

    [Theory]
    [InlineData(new[] { "MyApp", "--target-framework", "net10.0", "--no-http" }, "MyApp")]
    [InlineData(new[] { "--target-framework", "net10.0", "--no-http" }, null)]
    [InlineData(new[] { "--no-http", "--target-framework", "net10.0" }, null)]
    public void UnmatchedTokensAllowed_LeavesOptionLikeTokensUnmatchedInOrder(string[] args, string? expectedPath)
    {
        UnmatchedTokensAllowedCommand cmd = new();
        var parseResult = cmd.Parse(args);

        parseResult.Errors.Should().BeEmpty();
        parseResult.UnmatchedTokens.Should().Equal(expectedPath is null ? args : args[1..]);
        parseResult.GetValue(cmd.PathArgument!)!.OriginalPath.Should().Be(expectedPath);
    }
    private sealed class TestableCommand : FuncCliCommand
    {
        public TestableCommand() : base("testable", "Testable command for [path] parsing.")
        {
            AddPathArgument();
        }

        protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }

    private sealed class UnmatchedTokensAllowedCommand : FuncCliCommand
    {
        public UnmatchedTokensAllowedCommand()
            : base("unmatched", "Command that leaves unmatched tokens to its action.")
        {
            TreatUnmatchedTokensAsErrors = false;
            AddPathArgument();
        }

        protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}
