// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class HostProcessStartInfoFactoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DirectoryInfo _startupDirectory;
    private readonly DirectoryInfo _contentRoot;

    public HostProcessStartInfoFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-host-process-{Guid.NewGuid():N}");
        _startupDirectory = Directory.CreateDirectory(Path.Combine(_tempDir, "project", "bin"));
        _contentRoot = Directory.CreateDirectory(Path.Combine(_tempDir, "workload", "tools", "any"));
        File.WriteAllText(Path.Combine(_contentRoot.FullName, GetExecutableName()), string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Create_UsesPreparedStartupDirectoryEnvironmentAndDefaultPort()
    {
        var factory = new HostProcessStartInfoFactory();
        HostProcessStartContext context = CreateContext(
            port: null,
            enableAuth: true,
            environmentVariables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CUSTOM_ENV"] = "custom-value",
                [HostProcessStartInfoFactory.ScriptRootEnvironmentVariable] = "stale-value",
            });

        HostProcessLaunchInfo launchInfo = factory.Create(context);

        Assert.Equal(Path.Combine(_contentRoot.FullName, GetExecutableName()), launchInfo.StartInfo.FileName);
        Assert.Equal(_startupDirectory.FullName, launchInfo.StartInfo.WorkingDirectory);
        Assert.False(launchInfo.StartInfo.UseShellExecute);
        Assert.True(launchInfo.StartInfo.RedirectStandardInput);
        Assert.True(launchInfo.StartInfo.RedirectStandardOutput);
        Assert.True(launchInfo.StartInfo.RedirectStandardError);
        Assert.Equal(HostProcessStartInfoFactory.DefaultPort, launchInfo.Port);
        Assert.Equal(new Uri("http://0.0.0.0:7071"), launchInfo.ListenUri);
        Assert.Equal(new Uri("http://localhost:7071"), launchInfo.LocalBaseUri);
        Assert.Equal(["--enable-auth", "--urls", "http://0.0.0.0:7071"], launchInfo.StartInfo.ArgumentList);
        Assert.Equal("custom-value", launchInfo.StartInfo.Environment["CUSTOM_ENV"]);
        Assert.Equal(_startupDirectory.FullName, launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.ScriptRootEnvironmentVariable]);
        Assert.Equal("Development", launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.AzureFunctionsEnvironmentVariable]);
        Assert.Equal("localhost:7071", launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.WebsiteHostnameEnvironmentVariable]);
        Assert.Equal("true", launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.AspNetCoreSuppressStatusMessagesEnvironmentVariable]);
        Assert.Equal("None", launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.HostingLifetimeLogLevelEnvironmentVariable]);
    }

    [Fact]
    public void Create_UsesExplicitPortAndOnlyForwardsSupportedArguments()
    {
        var factory = new HostProcessStartInfoFactory();
        HostProcessStartContext context = CreateContext(
            port: 9090,
            enableAuth: false,
            cors: ["http://example.test"],
            functions: ["HttpTrigger"]);

        HostProcessLaunchInfo launchInfo = factory.Create(context);

        Assert.Equal(9090, launchInfo.Port);
        Assert.Equal(new Uri("http://0.0.0.0:9090"), launchInfo.ListenUri);
        Assert.Equal(new Uri("http://localhost:9090"), launchInfo.LocalBaseUri);
        Assert.Equal(["--urls", "http://0.0.0.0:9090"], launchInfo.StartInfo.ArgumentList);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Create_WhenPortIsOutOfRange_ThrowsGracefulException(int port)
    {
        var factory = new HostProcessStartInfoFactory();
        HostProcessStartContext context = CreateContext(port: port, enableAuth: false);

        var exception = Assert.Throws<GracefulException>(() => factory.Create(context));

        Assert.Contains("--port", exception.Message);
    }

    private HostProcessStartContext CreateContext(
        int? port,
        bool enableAuth,
        IReadOnlyList<string>? cors = null,
        IReadOnlyList<string>? functions = null,
        IDictionary<string, string>? environmentVariables = null)
    {
        environmentVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hostRunContext = new FunctionsProjectHostRunContext(
            _startupDirectory,
            "dotnet-isolated",
            environmentVariables);

        return new HostProcessStartContext(
            CreateHostWorkload(),
            hostRunContext,
            new StartCommandOptions(
                WorkingDirectory.FromExplicit(_startupDirectory.FullName),
                port,
                cors ?? [],
                true,
                functions ?? [],
                false,
                enableAuth,
                null,
                null,
                false,
                OutputMode.Plain,
                true,
                null,
                false,
                0,
                1.0,
                true));
    }

    private ContentWorkloadInfo CreateHostWorkload()
        => new(
            PackageId: "Azure.Functions.Cli.Workloads.Host.win-x64",
            PackageVersion: "4.1000.0",
            Aliases: ["host"],
            InstallDirectory: Path.Combine(_tempDir, "workload"),
            ContentRoot: _contentRoot.FullName,
            DisplayName: "Azure Functions host",
            Description: string.Empty);

    private static string GetExecutableName()
        => OperatingSystem.IsWindows()
            ? $"{HostProcessStartInfoFactory.ExecutableBaseName}.exe"
            : HostProcessStartInfoFactory.ExecutableBaseName;
}
