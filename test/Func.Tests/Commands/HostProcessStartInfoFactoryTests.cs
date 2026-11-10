// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Net;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.Logging;
using NSubstitute;

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

        launchInfo.StartInfo.FileName.Should().Be(Path.Combine(_contentRoot.FullName, GetExecutableName()));
        launchInfo.StartInfo.WorkingDirectory.Should().Be(_startupDirectory.FullName);
        launchInfo.StartInfo.UseShellExecute.Should().BeFalse();
        launchInfo.StartInfo.RedirectStandardInput.Should().BeTrue();
        launchInfo.StartInfo.RedirectStandardOutput.Should().BeTrue();
        launchInfo.StartInfo.RedirectStandardError.Should().BeTrue();
        launchInfo.Port.Should().Be(HostProcessStartInfoFactory.DefaultPort);
        launchInfo.ListenUri.Should().Be(new Uri("http://127.0.0.1:7071"));
        launchInfo.LocalBaseUri.Should().Be(new Uri("http://localhost:7071"));
        launchInfo.StartInfo.ArgumentList.Should().Equal(["--enable-auth", "--urls", "http://127.0.0.1:7071"]);
        launchInfo.StartInfo.Environment["CUSTOM_ENV"].Should().Be("custom-value");
        launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.ScriptRootEnvironmentVariable].Should().Be(_startupDirectory.FullName);
        launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.AzureFunctionsEnvironmentVariable].Should().Be("Development");
        launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.WebsiteHostnameEnvironmentVariable].Should().Be("localhost:7071");
        launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.AspNetCoreSuppressStatusMessagesEnvironmentVariable].Should().Be("true");
        launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.HostingLifetimeLogLevelEnvironmentVariable].Should().Be("None");
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

        launchInfo.Port.Should().Be(9090);
        launchInfo.ListenUri.Should().Be(new Uri("http://127.0.0.1:9090"));
        launchInfo.LocalBaseUri.Should().Be(new Uri("http://localhost:9090"));
        launchInfo.StartInfo.ArgumentList.Should().Equal(["--urls", "http://127.0.0.1:9090"]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Create_WhenPortIsOutOfRange_ThrowsGracefulException(int port)
    {
        var factory = new HostProcessStartInfoFactory();
        HostProcessStartContext context = CreateContext(port: port, enableAuth: false);

        var exception = FluentActions.Invoking(() => factory.Create(context)).Should().ThrowExactly<GracefulException>().Which;

        exception.Message.Should().Contain("--port");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65535)]
    public void Create_AcceptsPortBoundaries(int port)
    {
        var factory = new HostProcessStartInfoFactory();

        HostProcessLaunchInfo launchInfo = factory.Create(CreateContext(port, enableAuth: false));

        launchInfo.Port.Should().Be(port);
        launchInfo.ListenUri.Port.Should().Be(port);
        launchInfo.ListenUri.Host.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task StartAsync_ZeroPort_DoesNotProbeOrAdvertiseAReservedPort()
    {
        var context = CreateContext(0, enableAuth: false, environmentVariables: new Dictionary<string, string>
        {
            [HostProcessStartInfoFactory.WebsiteHostnameEnvironmentVariable] = "localhost:1234",
        });
        var factory = new HostProcessStartInfoFactory();
        HostProcessLaunchInfo launchInfo = factory.Create(context);
        var availability = Substitute.For<IHostPortAvailability>();
        var processFactory = Substitute.For<IHostProcessFactory>();
        var process = Substitute.For<IHostProcess>();
        process.StandardOutput.Returns(new StringReader(string.Empty));
        process.StandardError.Returns(new StringReader(string.Empty));
        process.StandardInput.Returns(new StringWriter());
        process.HasExited.Returns(true);
        process.WaitForExitAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        processFactory.Create(Arg.Any<ProcessStartInfo>()).Returns(process);
        var runner = new DefaultHostProcessRunner(factory, availability, processFactory,
            new LineHostProcessOutputParser(), Substitute.For<ILogger<DefaultHostProcessRunner>>());

        var stream = await runner.StartAsync(context, CancellationToken.None);
        await using var lifetime = (IAsyncDisposable)stream;
        var entries = new List<HostLogEntry>();
        await foreach (var entry in stream.ReadAsync(CancellationToken.None))
        {
            entries.Add(entry);
        }

        availability.DidNotReceive().IsAvailable(Arg.Any<IPAddress>(), Arg.Any<int>());
        process.Received(1).Start();
        launchInfo.StartInfo.ArgumentList.Should().Equal("--urls", "http://127.0.0.1:0");
        launchInfo.StartInfo.Environment.Should().NotContainKey(HostProcessStartInfoFactory.WebsiteHostnameEnvironmentVariable);
        entries.Should().ContainSingle().Which.Attributes.Should().NotContainKey("host.listen_uri");
        entries[0].Message.Should().NotContain("localhost:0");
    }

    [Theory]
    [InlineData(0, "0.0.0.0", "http://0.0.0.0:0")]
    [InlineData(9090, "0.0.0.0", "http://0.0.0.0:9090")]
    [InlineData(0, "::1", "http://[::1]:0")]
    [InlineData(9090, "::", "http://[::]:9090")]
    public void Create_ExplicitAddress_UsesRequestedInterface(int port, string address, string expectedUri)
    {
        var factory = new HostProcessStartInfoFactory();
        var context = CreateContext(port, enableAuth: false);
        context = context with { Options = context.Options with { Address = address } };

        HostProcessLaunchInfo launchInfo = factory.Create(context);

        launchInfo.ListenUri.Should().Be(new Uri(expectedUri));
        launchInfo.StartInfo.ArgumentList.Should().Equal("--urls", expectedUri);
    }

    [Theory]
    [InlineData(0, "127.0.0.2", "http://127.0.0.2:0/")]
    [InlineData(9090, "127.0.0.2", "http://127.0.0.2:9090/")]
    [InlineData(9090, "192.0.2.1", "http://192.0.2.1:9090/")]
    [InlineData(9090, "::1", "http://localhost:9090/")]
    public void Create_ExplicitAddress_PreservesReachableLocalUrl(int port, string address, string expectedUri)
    {
        var factory = new HostProcessStartInfoFactory();
        var context = CreateContext(port, enableAuth: false);
        context = context with { Options = context.Options with { Address = address } };

        HostProcessLaunchInfo launchInfo = factory.Create(context);

        launchInfo.LocalBaseUri.Should().Be(new Uri(expectedUri));
        if (port != 0)
        {
            launchInfo.StartInfo.Environment[HostProcessStartInfoFactory.WebsiteHostnameEnvironmentVariable]
                .Should().Be(new Uri(expectedUri).Authority);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("not-an-ip")]
    [InlineData("http://127.0.0.1")]
    public void Create_InvalidAddress_ThrowsUserError(string address)
    {
        var factory = new HostProcessStartInfoFactory();
        var context = CreateContext(0, enableAuth: false);
        context = context with { Options = context.Options with { Address = address } };

        Action act = () => factory.Create(context);

        var exception = act.Should().ThrowExactly<GracefulException>().Which;
        exception.Message.Should().Contain("--address").And.Contain("Host.LocalHttpAddress");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    public async Task StartAsync_FixedPort_ProbesConfiguredInterface(string address)
    {
        var context = CreateContext(9090, enableAuth: false);
        context = context with { Options = context.Options with { Address = address } };
        var availability = Substitute.For<IHostPortAvailability>();
        var processFactory = Substitute.For<IHostProcessFactory>();
        var runner = new DefaultHostProcessRunner(new HostProcessStartInfoFactory(), availability, processFactory,
            new LineHostProcessOutputParser(), Substitute.For<ILogger<DefaultHostProcessRunner>>());

        Func<Task> act = async () => await runner.StartAsync(context, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<GracefulException>();
        availability.Received(1).IsAvailable(IPAddress.Parse(address), 9090);
        processFactory.DidNotReceive().Create(Arg.Any<ProcessStartInfo>());
    }

    private HostProcessStartContext CreateContext(
        int? port,
        bool enableAuth,
        IReadOnlyList<string>? cors = null,
        IReadOnlyList<string>? functions = null,
        IDictionary<string, string>? environmentVariables = null)
    {
        environmentVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hostRunContext = new FunctionsProjectHostRunContext(_startupDirectory, "dotnet-isolated", environmentVariables);

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
