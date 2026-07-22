// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;

namespace Azure.Functions.Cli.Tests.Common;

[Collection("FuncHomeEnvVarTests")]
public class FuncHomeResolverTests
{
    [Fact]
    public void Resolve_WithoutEnvVar_ReturnsUserProfileDefault()
    {
        string expected = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName));

        string resolved = WithEnv(null, FuncHomeResolver.Resolve);

        resolved.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithBlankEnvVar_FallsBackToUserProfileDefault(string blank)
    {
        string expected = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName));

        string resolved = WithEnv(blank, FuncHomeResolver.Resolve);

        resolved.Should().Be(expected);
    }

    [Fact]
    public void Resolve_WithEnvVar_ReturnsNormalisedOverride()
    {
        string custom = Path.Combine(Path.GetTempPath(), "func-home-" + Guid.NewGuid().ToString("N"));

        string resolved = WithEnv(custom, FuncHomeResolver.Resolve);

        resolved.Should().Be(Path.GetFullPath(custom));
    }

    [Fact]
    public void Resolve_WithRelativeEnvVar_NormalisesToFullPath()
    {
        string relative = Path.Combine("relative", "func-home");

        string resolved = WithEnv(relative, FuncHomeResolver.Resolve);

        resolved.Should().Be(Path.GetFullPath(relative));
    }

    private static string WithEnv(string? value, Func<string> action)
    {
        string? previous = Environment.GetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable, value);
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Constants.FuncHomeEnvironmentVariable, previous);
        }
    }
}
