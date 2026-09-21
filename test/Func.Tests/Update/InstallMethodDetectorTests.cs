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
    [InlineData("C:\\Program Files\\Microsoft\\Azure Functions Core Tools\\func.exe", (int)InstallMethodKind.Winget, "winget", "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.")]
    [InlineData("C:\\Program Files\\WindowsApps\\Microsoft.AzureFunctionsCoreTools_5.0.0_x64__8wekyb3d8bbwe\\func.exe", (int)InstallMethodKind.Winget, "winget", "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.")]
    public void Detect_KnownPackageManagerPath_ReturnsMatchingMethod(
        string processPath,
        int expectedKindValue,
        string expectedDisplayName,
        string expectedUpdateInstruction)
    {
        var expectedKind = (InstallMethodKind)expectedKindValue;
        var detector = new InstallMethodDetector(CreateOptions(processPath), Substitute.For<IProcessEnvironment>());

        InstallMethod result = detector.Detect();

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedDisplayName, result.DisplayName);
        Assert.Equal(expectedUpdateInstruction, result.UpdateInstruction);
    }

    [Theory]
    [InlineData("/home/user/.azure-functions/func", "HOME", "/home/user")]
    [InlineData("C:\\Users\\me\\.azure-functions\\func.exe", "USERPROFILE", "C:\\Users\\me")]
    public void Detect_DefaultInstallScriptPath_ReturnsDirect(string processPath, string homeVariable, string homePath)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(homeVariable).Returns(homePath);
        var detector = new InstallMethodDetector(CreateOptions(processPath), environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
        Assert.Null(result.UpdateInstruction);
    }

    [Fact]
    public void Detect_OverriddenInstallScriptPath_ReturnsDirect()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns("/opt/azure-functions-cli");
        var detector = new InstallMethodDetector(CreateOptions("/opt/azure-functions-cli/func"), environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Theory]
    [InlineData("/home/user/tools/func")]
    [InlineData("C:\\Program Files\\Azure Functions CLI\\func.exe")]
    [InlineData(null)]
    public void Detect_UnknownInstallPath_ThrowsGraceful(string? processPath)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("HOME").Returns("/home/user");
        var detector = new InstallMethodDetector(CreateOptions(processPath), environment);

        GracefulException exception = Assert.Throws<GracefulException>(detector.Detect);

        Assert.True(exception.IsUserError);
        Assert.Contains("https://aka.ms/func-cli", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new InstallMethodDetector(null!, Substitute.For<IProcessEnvironment>()));
    }

    [Fact]
    public void Constructor_NullProcessEnvironment_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new InstallMethodDetector(CreateOptions("/home/user/.azure-functions/func"), null!));
    }

    private static IOptions<CliEnvironmentOptions> CreateOptions(string? processPath)
    {
        var opts = new CliEnvironmentOptions { ProcessPath = processPath! };
        return Options.Create(opts);
    }
}
