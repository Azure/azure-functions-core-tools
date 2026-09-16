// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class InstallMethodDetectorTests
{
    [Theory]
    [InlineData("/usr/local/lib/node_modules/azure-functions-core-tools/bin/func", (int)InstallMethodKind.Npm, "npm", "Reinstall Azure Functions CLI with the v5 installer at https://aka.ms/func-cli.")]
    [InlineData("C:\\Users\\me\\AppData\\Roaming\\npm\\node_modules\\azure-functions-core-tools\\bin\\func.exe", (int)InstallMethodKind.Npm, "npm", "Reinstall Azure Functions CLI with the v5 installer at https://aka.ms/func-cli.")]
    [InlineData("/opt/homebrew/Cellar/azure-functions-core-tools/4.0.5000/func", (int)InstallMethodKind.Homebrew, "Homebrew", "Run 'brew upgrade azure-functions-core-tools' to update.")]
    [InlineData("/usr/local/Cellar/azure-functions-core-tools/4.0.5000/func", (int)InstallMethodKind.Homebrew, "Homebrew", "Run 'brew upgrade azure-functions-core-tools' to update.")]
    [InlineData("/home/linuxbrew/.linuxbrew/Cellar/azure-functions-core-tools/4.0.5000/func", (int)InstallMethodKind.Homebrew, "Homebrew", "Run 'brew upgrade azure-functions-core-tools' to update.")]
    [InlineData("C:\\ProgramData\\chocolatey\\lib\\azure-functions-core-tools\\tools\\func.exe", (int)InstallMethodKind.Chocolatey, "Chocolatey", "Run 'choco upgrade azure-functions-core-tools' to update.")]
    [InlineData("C:\\Users\\me\\AppData\\Local\\Microsoft\\WinGet\\Packages\\Microsoft.AzureFunctionsCoreTools_Microsoft.Winget.Source_8wekyb3d8bbwe\\func.exe", (int)InstallMethodKind.Winget, "winget", "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.")]
    public void Detect_KnownPackageManagerPath_ReturnsMatchingMethod(
        string processPath,
        int expectedKindValue,
        string expectedDisplayName,
        string expectedUpdateInstruction)
    {
        var expectedKind = (InstallMethodKind)expectedKindValue;
        var detector = new InstallMethodDetector(CreateOptions(processPath));

        InstallMethod result = detector.Detect();

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedDisplayName, result.DisplayName);
        Assert.Equal(expectedUpdateInstruction, result.UpdateInstruction);
    }

    [Theory]
    [InlineData("/opt/azure-functions-cli/func")]
    [InlineData("C:\\Program Files\\Azure Functions CLI\\func.exe")]
    [InlineData("/home/user/tools/func")]
    public void Detect_DirectInstallPath_ReturnsDirect(string processPath)
    {
        var detector = new InstallMethodDetector(CreateOptions(processPath));

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
        Assert.Null(result.UpdateInstruction);
    }

    [Fact]
    public void Detect_NullProcessPath_ReturnsDirect()
    {
        var detector = new InstallMethodDetector(CreateOptions(null));

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Fact]
    public void Constructor_NullEnvironment_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new InstallMethodDetector(null!));
    }

    private static IOptions<CliEnvironmentOptions> CreateOptions(string? processPath)
    {
        var opts = new CliEnvironmentOptions { ProcessPath = processPath! };
        return Options.Create(opts);
    }
}
