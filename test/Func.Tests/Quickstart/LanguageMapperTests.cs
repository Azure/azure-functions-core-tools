// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;
using Xunit;

namespace Azure.Functions.Cli.Tests.Quickstart;

public class LanguageMapperTests
{
    [Theory]
    [InlineData("csharp", "CSharp")]
    [InlineData("CSharp", "CSharp")]
    [InlineData("fsharp", "FSharp")]
    [InlineData("javascript", "JavaScript")]
    [InlineData("typescript", "TypeScript")]
    [InlineData("python", "Python")]
    [InlineData("java", "Java")]
    [InlineData("powershell", "PowerShell")]
    [InlineData("dotnet", "CSharp")]
    [InlineData("node", "TypeScript")]
    public void ToManifestLanguage_KnownFlag_ReturnsMappedLanguage(string flag, string expected)
    {
        Assert.Equal(expected, LanguageMapper.ToManifestLanguage(flag));
    }

    [Fact]
    public void ToManifestLanguage_DotnetIsolated_ReturnsNull()
    {
        // `dotnet-isolated` is intentionally not exposed; only `dotnet`.
        Assert.Null(LanguageMapper.ToManifestLanguage("dotnet-isolated"));
    }

    [Fact]
    public void ToManifestLanguage_UnknownFlag_ReturnsNull()
    {
        Assert.Null(LanguageMapper.ToManifestLanguage("ruby"));
    }

    [Fact]
    public void GetLanguagesForRuntime_Dotnet_ReturnsCSharpAndFSharp()
    {
        var result = LanguageMapper.GetLanguagesForRuntime("dotnet");
        Assert.Equal(["CSharp", "FSharp"], result);
    }

    [Fact]
    public void GetLanguagesForRuntime_DotnetIsolated_ReturnsEmpty()
    {
        // We only expose `dotnet`; `dotnet-isolated` is not a public runtime alias.
        Assert.Empty(LanguageMapper.GetLanguagesForRuntime("dotnet-isolated"));
    }

    [Fact]
    public void GetLanguagesForRuntime_Node_ReturnsTypeScriptAndJavaScript()
    {
        var result = LanguageMapper.GetLanguagesForRuntime("node");
        Assert.Equal(["TypeScript", "JavaScript"], result);
    }

    [Theory]
    [InlineData("python", "Python")]
    [InlineData("java", "Java")]
    [InlineData("powershell", "PowerShell")]
    public void GetLanguagesForRuntime_SingleLanguageRuntime_ReturnsOne(string runtime, string expected)
    {
        var result = LanguageMapper.GetLanguagesForRuntime(runtime);
        Assert.Equal([expected], result);
    }

    [Fact]
    public void GetLanguagesForRuntime_UnknownRuntime_ReturnsEmpty()
    {
        Assert.Empty(LanguageMapper.GetLanguagesForRuntime("rust"));
    }

    [Theory]
    [InlineData("dotnet", "CSharp")]
    [InlineData("node", "TypeScript")]
    [InlineData("python", "Python")]
    public void DefaultLanguageForRuntime_KnownRuntime_ReturnsDefault(string runtime, string expected)
    {
        Assert.Equal(expected, LanguageMapper.DefaultLanguageForRuntime(runtime));
    }

    [Fact]
    public void DefaultLanguageForRuntime_DotnetIsolated_ReturnsNull()
    {
        Assert.Null(LanguageMapper.DefaultLanguageForRuntime("dotnet-isolated"));
    }

    [Fact]
    public void DefaultLanguageForRuntime_UnknownRuntime_ReturnsNull()
    {
        Assert.Null(LanguageMapper.DefaultLanguageForRuntime("rust"));
    }

    [Fact]
    public void AllManifestLanguages_ContainsCoreLanguages()
    {
        var all = LanguageMapper.AllManifestLanguages;

        Assert.Contains("CSharp", all);
        Assert.Contains("FSharp", all);
        Assert.Contains("JavaScript", all);
        Assert.Contains("TypeScript", all);
        Assert.Contains("Python", all);
        Assert.Contains("Java", all);
        Assert.Contains("PowerShell", all);
    }
}
