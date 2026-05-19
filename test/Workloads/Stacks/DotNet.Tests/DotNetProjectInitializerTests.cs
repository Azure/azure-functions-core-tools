// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
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
    public void DefaultFramework_IsNet10()
    {
        Assert.Equal("net10.0", DotNetProjectInitializer.DefaultFramework);
    }

    [Theory]
    [InlineData(null, "c#")]
    [InlineData("", "c#")]
    [InlineData("  ", "c#")]
    [InlineData("csharp", "c#")]
    [InlineData("CSHARP", "c#")]
    [InlineData("C#", "c#")]
    [InlineData("fsharp", "f#")]
    [InlineData("FSHARP", "f#")]
    [InlineData("F#", "f#")]
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

    [Fact]
    public async Task EnsureTemplatesInstalledAsync_CallsInstallWhenTimestampMissing()
    {
        DotNetProjectInitializer initializer = CreateInitializer();

        string hivePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".azure-functions",
            "dotnet-template-hive");
        string timestampPath = Path.Combine(hivePath, ".installed");
        if (File.Exists(timestampPath))
        {
            File.Delete(timestampPath);
        }

        await initializer.EnsureTemplatesInstalledAsync(CancellationToken.None);

        await _dotnetCli.Received(1).RunAsync(
            Arg.Is<IReadOnlyList<string>>(args =>
                args.Contains(DotNetProjectInitializer.TemplatesPackageName)),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureTemplatesInstalledAsync_SkipsInstallWhenTimestampIsFresh()
    {
        string hivePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".azure-functions",
            "dotnet-template-hive");
        string timestampPath = Path.Combine(hivePath, ".installed");

        Directory.CreateDirectory(hivePath);
        File.WriteAllText(timestampPath, string.Empty);

        try
        {
            DotNetProjectInitializer initializer = CreateInitializer();

            await initializer.EnsureTemplatesInstalledAsync(CancellationToken.None);

            await _dotnetCli.DidNotReceive().RunAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(timestampPath);
        }
    }
}
