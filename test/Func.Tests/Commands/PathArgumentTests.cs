// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Xunit;

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

        Assert.NotNull(workingDirectory);
        Assert.True(workingDirectory!.WasExplicit);
        Assert.Equal("/tmp/some-project", workingDirectory.OriginalPath);
        Assert.EndsWith("some-project", workingDirectory.Info.Name);
    }

    [Fact]
    public void NoPath_FallsBackToCurrentDirectoryAndIsNotExplicit()
    {
        var cmd = new TestableCommand();
        var parseResult = cmd.Parse([]);

        var workingDirectory = parseResult.GetValue(cmd.PathArgument!);

        Assert.NotNull(workingDirectory);
        Assert.False(workingDirectory!.WasExplicit);
        Assert.Null(workingDirectory.OriginalPath);
        Assert.Equal(Directory.GetCurrentDirectory(), workingDirectory.Info.FullName);
    }

    [Fact]
    public void DashPrefix_SurfacesAsParseError()
    {
        var cmd = new TestableCommand();
        var parseResult = cmd.Parse(["--bogus"]);

        Assert.NotEmpty(parseResult.Errors);
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
}
