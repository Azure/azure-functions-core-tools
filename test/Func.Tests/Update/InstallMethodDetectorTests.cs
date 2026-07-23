// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class InstallMethodDetectorTests
{
    [Theory]
    [InlineData("/usr/local/lib/node_modules/azure-functions-core-tools/bin/func", (int)InstallMethodKind.Npm, "npm")]
    [InlineData("C:\\Users\\me\\AppData\\Roaming\\npm\\node_modules\\azure-functions-core-tools\\bin\\func.exe", (int)InstallMethodKind.Npm, "npm")]
    [InlineData("/opt/homebrew/Cellar/azure-functions-core-tools/4.0.5000/func", (int)InstallMethodKind.Homebrew, "Homebrew")]
    [InlineData("/usr/local/Cellar/azure-functions-core-tools/4.0.5000/func", (int)InstallMethodKind.Homebrew, "Homebrew")]
    [InlineData("/home/linuxbrew/.linuxbrew/Cellar/azure-functions-core-tools/4.0.5000/func", (int)InstallMethodKind.Homebrew, "Homebrew")]
    [InlineData("C:\\ProgramData\\chocolatey\\lib\\azure-functions-core-tools\\tools\\func.exe", (int)InstallMethodKind.Chocolatey, "Chocolatey")]
    [InlineData("C:\\Users\\me\\AppData\\Local\\Microsoft\\WinGet\\Packages\\Microsoft.AzureFunctionsCoreTools_Microsoft.Winget.Source_8wekyb3d8bbwe\\func.exe", (int)InstallMethodKind.Winget, "winget")]
    public void Detect_KnownPackageManagerPath_ReturnsMatchingMethod(string processPath, int expectedKindValue, string expectedDisplayName)
    {
        var expectedKind = (InstallMethodKind)expectedKindValue;
        ICliEnvironment environment = Substitute.For<ICliEnvironment>();
        environment.ProcessPath.Returns(processPath);
        var detector = new InstallMethodDetector(environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedDisplayName, result.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(result.UpgradeCommand));
    }

    [Theory]
    [InlineData("/opt/azure-functions-cli/func")]
    [InlineData("C:\\Program Files\\Azure Functions CLI\\func.exe")]
    [InlineData("/home/user/tools/func")]
    public void Detect_DirectInstallPath_ReturnsDirect(string processPath)
    {
        ICliEnvironment environment = Substitute.For<ICliEnvironment>();
        environment.ProcessPath.Returns(processPath);
        var detector = new InstallMethodDetector(environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
        Assert.Null(result.UpgradeCommand);
    }

    [Fact]
    public void Detect_NullProcessPath_ReturnsDirect()
    {
        ICliEnvironment environment = Substitute.For<ICliEnvironment>();
        environment.ProcessPath.Returns((string?)null);
        var detector = new InstallMethodDetector(environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Fact]
    public void Constructor_NullEnvironment_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new InstallMethodDetector(null!));
    }
}
