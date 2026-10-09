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
    [InlineData("/opt/homebrew/Cellar/azure-functions-core-tools@4/4.0.5000/func", (int)InstallMethodKind.Homebrew, "Homebrew", "Run 'brew upgrade azure-functions-core-tools@4' to update.")]
    [InlineData("/usr/local/Cellar/azure-functions-core-tools/4.0.5000/func", (int)InstallMethodKind.Homebrew, "Homebrew", "Run 'brew upgrade azure-functions-core-tools' to update.")]
    [InlineData("/home/linuxbrew/.linuxbrew/Cellar/azure-functions-core-tools/4.0.5000/func", (int)InstallMethodKind.Homebrew, "Homebrew", "Run 'brew upgrade azure-functions-core-tools' to update.")]
    [InlineData("C:\\Users\\me\\AppData\\Local\\Microsoft\\WinGet\\Packages\\Microsoft.AzureFunctionsCoreTools_Microsoft.Winget.Source_8wekyb3d8bbwe\\func.exe", (int)InstallMethodKind.Winget, "winget", "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.")]
    [InlineData("C:\\Users\\me\\AppData\\Local\\Microsoft\\WindowsApps\\func.exe", (int)InstallMethodKind.Winget, "winget", "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.")]
    [InlineData("C:\\Program Files\\Microsoft\\Azure Functions Core Tools\\func.exe", (int)InstallMethodKind.Winget, "winget", "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.")]
    [InlineData("C:\\Program Files\\WindowsApps\\Microsoft.AzureFunctionsCoreTools_5.0.0_x64__8wekyb3d8bbwe\\func.exe", (int)InstallMethodKind.Winget, "winget", "Run 'winget upgrade Microsoft.AzureFunctionsCoreTools' to update.")]
    public void Detect_KnownPackageManagerPath_ReturnsMatchingMethod(
        string processPath,
        int expectedKindValue,
        string expectedDisplayName,
        string expectedUpdateInstruction)
    {
        var expectedKind = (InstallMethodKind)expectedKindValue;
        InstallMethodDetector detector = CreateDetector(processPath, Substitute.For<IProcessEnvironment>());

        InstallMethod result = detector.Detect();

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedDisplayName, result.DisplayName);
        Assert.Equal(expectedUpdateInstruction, result.UpdateInstruction);
        Assert.Equal(processPath, result.ExecutablePath);
    }

    [Theory]
    [InlineData("/home/user/.azure-functions/func", "HOME", "/home/user")]
    [InlineData("C:\\Users\\me\\.azure-functions\\func.exe", "USERPROFILE", "C:\\Users\\me")]
    public void Detect_DefaultInstallScriptPath_ReturnsDirect(string processPath, string homeVariable, string homePath)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(homeVariable).Returns(homePath);
        InstallMethodDetector detector = CreateDetector(processPath, environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
        Assert.Null(result.UpdateInstruction);
        Assert.Equal(processPath, result.ExecutablePath);
    }

    [Theory]
    [InlineData("/home/linuxbrew")]
    [InlineData("/home/Cellar/azure-functions-core-tools/4.0.5000")]
    public void Detect_DefaultInstallScriptPathWithHomebrewInHomeDirectory_ReturnsDirect(string home)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("HOME").Returns(home);
        InstallMethodDetector detector = CreateDetector($"{home}/.azure-functions/func", environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
        Assert.Equal($"{home}/.azure-functions/func", result.ExecutablePath);
    }

    [Fact]
    public void Detect_CaseOnlyDifferentDefaultInstallDirectoryOnUnix_ThrowsDetectionException()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("HOME").Returns("/home/user");
        InstallMethodDetector detector = CreateDetector("/home/user/.AZURE-FUNCTIONS/func", environment);

        InstallMethodDetectionException exception = Assert.Throws<InstallMethodDetectionException>(detector.Detect);

        Assert.Contains("https://aka.ms/func-cli", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_CaseOnlyDifferentDefaultInstallDirectoryOnWindows_ReturnsDirect()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("USERPROFILE").Returns("C:\\Users\\Me");
        InstallMethodDetector detector = CreateDetector("c:\\users\\me\\.AZURE-FUNCTIONS\\func.exe", environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Fact]
    public void Detect_GenuinePackageManagerPathUnderConfiguredInstallDirectory_ReturnsPackageManager()
    {
        const string installDirectory = "/opt/homebrew/Cellar/azure-functions-core-tools/4.0.5000";
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns(installDirectory);
        InstallMethodDetector detector = CreateDetector($"{installDirectory}/func", environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Homebrew, result.Kind);
    }

    [Fact]
    public void Detect_OverriddenInstallScriptPath_ReturnsDirect()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns("/opt/azure-functions-cli");
        InstallMethodDetector detector = CreateDetector("/opt/azure-functions-cli/func", environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
        Assert.Equal("/opt/azure-functions-cli/func", result.ExecutablePath);
    }

    [Fact]
    public void Detect_PrefixCollision_ThrowsDetectionException()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("HOME").Returns("/home/user");
        InstallMethodDetector detector = CreateDetector("/home/user/.azure-functions-other/func", environment);

        Assert.Throws<InstallMethodDetectionException>(detector.Detect);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_MissingOrBlankHomeVariables_ThrowsDetectionException(string? value)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("HOME").Returns(value);
        environment.Get("USERPROFILE").Returns(value);
        InstallMethodDetector detector = CreateDetector("/home/user/.azure-functions/func", environment);

        Assert.Throws<InstallMethodDetectionException>(detector.Detect);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_BlankInstallDirectoryOverride_FallsBackToDefault(string value)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns(value);
        environment.Get("HOME").Returns("/home/user");
        InstallMethodDetector detector = CreateDetector("/home/user/.azure-functions/func", environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Theory]
    [InlineData("/opt/azure-functions-cli/")]
    [InlineData("/opt/azure-functions-cli////")]
    public void Detect_InstallDirectoryOverrideWithTrailingSeparator_ReturnsDirect(string installDirectory)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns(installDirectory);
        InstallMethodDetector detector = CreateDetector("/opt/azure-functions-cli/func", environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Fact]
    public void Detect_UnixPathWithHomeAndUserProfile_UsesHome()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("HOME").Returns("/home/unix-user");
        environment.Get("USERPROFILE").Returns("/home/windows-user");
        InstallMethodDetector detector = CreateDetector("/home/unix-user/.azure-functions/func", environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Fact]
    public void Detect_WindowsPathWithHomeAndUserProfile_UsesUserProfile()
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("HOME").Returns("C:\\Home");
        environment.Get("USERPROFILE").Returns("C:\\Users\\me");
        InstallMethodDetector detector = CreateDetector("C:\\Users\\me\\.azure-functions\\func.exe", environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Theory]
    [InlineData("/home/fallback/.azure-functions/func", "HOME", null, "USERPROFILE", "/home/fallback")]
    [InlineData("/home/fallback/.azure-functions/func", "HOME", "", "USERPROFILE", "/home/fallback")]
    [InlineData("/home/fallback/.azure-functions/func", "HOME", "   ", "USERPROFILE", "/home/fallback")]
    [InlineData("C:\\Fallback\\.azure-functions\\func.exe", "USERPROFILE", null, "HOME", "C:\\Fallback")]
    [InlineData("C:\\Fallback\\.azure-functions\\func.exe", "USERPROFILE", "", "HOME", "C:\\Fallback")]
    [InlineData("C:\\Fallback\\.azure-functions\\func.exe", "USERPROFILE", "   ", "HOME", "C:\\Fallback")]
    public void Detect_MissingOrBlankPrimaryHomeVariable_UsesPlatformFallback(
        string processPath,
        string primaryVariable,
        string? primaryValue,
        string fallbackVariable,
        string fallbackHome)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(primaryVariable).Returns(primaryValue);
        environment.Get(fallbackVariable).Returns(fallbackHome);
        InstallMethodDetector detector = CreateDetector(processPath, environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Theory]
    [InlineData("/opt/WindowsApps/tools/func", "/opt/WindowsApps/tools")]
    [InlineData("/opt/WinGet/Packages/tools/func", "/opt/WinGet/Packages/tools")]
    [InlineData(
        "/opt/Program Files/Microsoft/Azure Functions Core Tools/func",
        "/opt/Program Files/Microsoft/Azure Functions Core Tools")]
    public void Detect_UnixDirectPathContainingWindowsPackageMarker_ReturnsDirect(string processPath, string installDirectory)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns(installDirectory);
        InstallMethodDetector detector = CreateDetector(processPath, environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Theory]
    [InlineData("C:\\tools\\WindowsApps\\func.exe", "C:\\tools\\WindowsApps")]
    [InlineData("C:\\tools\\WinGet\\Packages\\func.exe", "C:\\tools\\WinGet\\Packages")]
    [InlineData(
        "\\\\server\\share\\Program Files\\Microsoft\\Azure Functions Core Tools\\func.exe",
        "\\\\server\\share\\Program Files\\Microsoft\\Azure Functions Core Tools")]
    public void Detect_WindowsDirectPathWithPackageManagerLookalike_ReturnsDirect(string processPath, string installDirectory)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns(installDirectory);
        InstallMethodDetector detector = CreateDetector(processPath, environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Theory]
    [InlineData("/opt/NODE_MODULES/azure-functions-core-tools/bin/func")]
    [InlineData("/opt/homebrew/cellar/azure-functions-core-tools/4.0.5000/func")]
    [InlineData("/opt/homebrew/Cellar/Azure-Functions-Core-Tools/4.0.5000/func")]
    [InlineData("/opt/homebrew/Cellar/azure-functions-core-tools/4.0.5000/FUNC")]
    public void Detect_CaseVariantPackageManagerPathOnUnix_ReturnsDirect(string processPath)
    {
        string installDirectory = processPath[..processPath.LastIndexOf('/')];
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns(installDirectory);
        InstallMethodDetector detector = CreateDetector(processPath, environment);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
    }

    [Fact]
    public void Detect_CanonicalAliases_ReturnsDirect()
    {
        const string processPath = "/alias/install/func";
        const string installDirectory = "/real/install";
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns("/alias/install");
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        fileSystem.GetCanonicalPath(processPath).Returns($"{installDirectory}/func");
        fileSystem.GetCanonicalPath("/alias/install").Returns(installDirectory);
        var detector = new InstallMethodDetector(CreateOptions(processPath), environment, fileSystem);

        InstallMethod result = detector.Detect();

        Assert.Equal(InstallMethodKind.Direct, result.Kind);
        Assert.Equal($"{installDirectory}/func", result.ExecutablePath);
    }

    [Fact]
    public void Detect_PathCanonicalizationFailure_ThrowsDetectionExceptionWithCause()
    {
        const string processPath = "/alias/install/func";
        var cause = new IOException("broken link");
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        fileSystem.GetCanonicalPath(processPath).Returns(_ => throw cause);
        var detector = new InstallMethodDetector(
            CreateOptions(processPath),
            Substitute.For<IProcessEnvironment>(),
            fileSystem);

        InstallMethodDetectionException exception = Assert.Throws<InstallMethodDetectionException>(detector.Detect);

        Assert.Contains(processPath, exception.Message, StringComparison.Ordinal);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public void Detect_ExecutableUnderSymlinkedDirectoryOnUnix_ReturnsDirect()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string installDirectory = Path.Combine(root, "install");
        string installAlias = Path.Combine(root, "install-link");
        string executablePath = Path.Combine(installDirectory, "func");
        Directory.CreateDirectory(installDirectory);
        File.WriteAllText(executablePath, string.Empty);
        Directory.CreateSymbolicLink(installAlias, installDirectory);

        try
        {
            IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
            environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns(installDirectory);
            var detector = new InstallMethodDetector(
                CreateOptions(Path.Combine(installAlias, "func")),
                environment,
                new PhysicalFileSystem());

            InstallMethod result = detector.Detect();

            Assert.Equal(InstallMethodKind.Direct, result.Kind);
            Assert.Equal(executablePath, result.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_SymbolicLinkTargetWithIntermediateLinkOnUnix_ReturnsCanonicalExecutablePath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string physicalRoot = Path.Combine(root, "physical");
        string installDirectory = Path.Combine(physicalRoot, "install");
        string intermediateAlias = Path.Combine(root, "physical-link");
        string installAlias = Path.Combine(root, "install-link");
        string executablePath = Path.Combine(installDirectory, "func");
        Directory.CreateDirectory(installDirectory);
        File.WriteAllText(executablePath, string.Empty);
        Directory.CreateSymbolicLink(intermediateAlias, physicalRoot);
        Directory.CreateSymbolicLink(installAlias, Path.Combine(intermediateAlias, "install"));

        try
        {
            IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
            environment.Get(InstallMethodDetector.InstallDirectoryEnvironmentVariable).Returns(installDirectory);
            var detector = new InstallMethodDetector(
                CreateOptions(Path.Combine(installAlias, "func")),
                environment,
                new PhysicalFileSystem());

            InstallMethod result = detector.Detect();

            Assert.Equal(InstallMethodKind.Direct, result.Kind);
            Assert.Equal(executablePath, result.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_HomebrewExecutableSymlinkOnUnix_ReturnsVersionedFormula()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string cellarDirectory = Path.Combine(root, "Cellar", "azure-functions-core-tools@4", "4.0.5000");
        string executablePath = Path.Combine(cellarDirectory, "func");
        string aliasDirectory = Path.Combine(root, "bin");
        string aliasExecutable = Path.Combine(aliasDirectory, "func");
        Directory.CreateDirectory(cellarDirectory);
        Directory.CreateDirectory(aliasDirectory);
        File.WriteAllText(executablePath, string.Empty);
        File.CreateSymbolicLink(aliasExecutable, executablePath);

        try
        {
            var detector = new InstallMethodDetector(
                CreateOptions(aliasExecutable),
                Substitute.For<IProcessEnvironment>(),
                new PhysicalFileSystem());

            InstallMethod result = detector.Detect();

            Assert.Equal(InstallMethodKind.Homebrew, result.Kind);
            Assert.Equal("Run 'brew upgrade azure-functions-core-tools@4' to update.", result.UpdateInstruction);
            Assert.Equal(executablePath, result.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("/home/user/tools/func")]
    [InlineData("C:\\ProgramData\\chocolatey\\lib\\azure-functions-core-tools\\tools\\func.exe")]
    [InlineData("C:\\Program Files\\Azure Functions CLI\\func.exe")]
    [InlineData(null)]
    public void Detect_UnknownInstallPath_ThrowsDetectionException(string? processPath)
    {
        IProcessEnvironment environment = Substitute.For<IProcessEnvironment>();
        environment.Get("HOME").Returns("/home/user");
        InstallMethodDetector detector = CreateDetector(processPath, environment);

        InstallMethodDetectionException exception = Assert.Throws<InstallMethodDetectionException>(detector.Detect);

        Assert.Contains("https://aka.ms/func-cli", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/opt/homebrew/Cellar/azure-functions-core-tools@/4.0.5000/func")]
    [InlineData("/opt/homebrew/Cellar/azure-functions-core-tools@latest/4.0.5000/func")]
    [InlineData("/opt/homebrew/Cellar/azure-functions-core-tools@4-beta/4.0.5000/func")]
    public void Detect_MalformedVersionedHomebrewFormula_ThrowsDetectionException(string processPath)
    {
        InstallMethodDetector detector = CreateDetector(processPath, Substitute.For<IProcessEnvironment>());

        Assert.Throws<InstallMethodDetectionException>(detector.Detect);
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new InstallMethodDetector(null!, Substitute.For<IProcessEnvironment>(), Substitute.For<IFileSystem>()));
    }

    [Fact]
    public void Constructor_NullProcessEnvironment_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new InstallMethodDetector(CreateOptions("/home/user/.azure-functions/func"), null!, Substitute.For<IFileSystem>()));
    }

    [Fact]
    public void Constructor_NullFileSystem_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new InstallMethodDetector(
                CreateOptions("/home/user/.azure-functions/func"),
                Substitute.For<IProcessEnvironment>(),
                null!));
    }

    private static InstallMethodDetector CreateDetector(string? processPath, IProcessEnvironment environment)
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        fileSystem.GetCanonicalPath(Arg.Any<string>()).Returns(call => call.Arg<string>());
        return new InstallMethodDetector(CreateOptions(processPath), environment, fileSystem);
    }

    private static IOptions<CliEnvironmentOptions> CreateOptions(string? processPath)
    {
        var opts = new CliEnvironmentOptions { ProcessPath = processPath! };
        return Options.Create(opts);
    }
}
