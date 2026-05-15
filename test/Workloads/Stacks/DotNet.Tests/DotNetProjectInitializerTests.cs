// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetProjectInitializerTests
{
    private readonly IDotnetCliRunner _dotnetCli = Substitute.For<IDotnetCliRunner>();

    private DotNetProjectInitializer CreateInitializer() => new(_dotnetCli);

    [Fact]
    public void Stack_IsDotNet()
    {
        Assert.Equal("dotnet", CreateInitializer().Stack);
    }

    [Fact]
    public void GetInitOptions_ContainsFrameworkOption()
    {
        DotNetProjectInitializer initializer = CreateInitializer();
        IReadOnlyList<Option> options = initializer.GetInitOptions();

        Assert.Single(options);
        Assert.Same(initializer.FrameworkOption, options[0]);
    }

    [Fact]
    public void SupportedLanguages_ReturnsExpectedLanguages()
    {
        Assert.Equal(["C#", "F#", "csharp", "fsharp"], CreateInitializer().SupportedLanguages);
    }

    [Fact]
    public void TemplatesPackageName_IsCorrect()
    {
        Assert.Equal("Microsoft.Azure.Functions.Worker.ProjectTemplates", DotNetProjectInitializer.TemplatesPackageName);
    }

    [Fact]
    public void TemplatesPackageVersion_IsCorrect()
    {
        Assert.Equal("4.0.5544", DotNetProjectInitializer.TemplatesPackageVersion);
    }

    [Fact]
    public void DefaultFramework_IsNet10()
    {
        Assert.Equal("net10.0", DotNetProjectInitializer.DefaultFramework);
    }

    [Theory]
    [InlineData(null, "C#")]
    [InlineData("", "C#")]
    [InlineData("  ", "C#")]
    [InlineData("csharp", "C#")]
    [InlineData("CSHARP", "C#")]
    [InlineData("C#", "C#")]
    [InlineData("fsharp", "F#")]
    [InlineData("FSHARP", "F#")]
    [InlineData("F#", "F#")]
    [InlineData("unknown", "unknown")]
    public void NormalizeLanguage_ReturnsExpectedResult(string? input, string expected)
    {
        Assert.Equal(expected, DotNetProjectInitializer.NormalizeLanguage(input));
    }

    [Fact]
    public void Constructor_ThrowsOnNullRunner()
    {
        Assert.Throws<ArgumentNullException>(() => new DotNetProjectInitializer(null!));
    }
}
