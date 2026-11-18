// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class WorkloadTemplateCacheInvalidatorTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly ILogger<WorkloadTemplateCacheInvalidator> _logger = Substitute.For<ILogger<WorkloadTemplateCacheInvalidator>>();
    private readonly CliConfigurationPathsOptions _paths = new(Path.Combine(Path.GetTempPath(), "func-invalidator-home"));
    private readonly string _hostDirectory;
    private readonly WorkloadTemplateCacheInvalidator _invalidator;

    public WorkloadTemplateCacheInvalidatorTests()
    {
        _hostDirectory = Path.Combine(_paths.Home, TemplaterFactory.SettingsDirectoryName, FuncTemplateEngineHost.Identifier);
        _invalidator = new WorkloadTemplateCacheInvalidator(_paths, _fileSystem, _logger);
    }

    [Fact]
    public void Constructor_NullDependency_Throws()
    {
        FluentActions.Invoking(() => new WorkloadTemplateCacheInvalidator(null!, _fileSystem, _logger))
            .Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkloadTemplateCacheInvalidator(_paths, null!, _logger))
            .Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => new WorkloadTemplateCacheInvalidator(_paths, _fileSystem, null!))
            .Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public async Task OnWorkloadsChangedAsync_MissingHostDirectory_DoesNothing()
    {
        _fileSystem.DirectoryExists(_hostDirectory).Returns(false);

        await _invalidator.OnWorkloadsChangedAsync(CancellationToken.None);

        _fileSystem.DidNotReceiveWithAnyArgs().GetFiles(default!);
        _fileSystem.DidNotReceiveWithAnyArgs().DeleteFile(default!);
    }

    [Fact]
    public async Task OnWorkloadsChangedAsync_DeletesOnlyTemplateCachesInEveryVersionDirectory()
    {
        string firstCache = Path.Combine(_hostDirectory, "1.0.0", "templatecache.json");
        string secondCache = Path.Combine(_hostDirectory, "2.0.0", "TemplateCache.json");
        string[] others =
        [
            Path.Combine(_hostDirectory, "1.0.0", "templatecache.json.bak"),
            Path.Combine(_hostDirectory, "2.0.0", "settings.json"),
        ];
        _fileSystem.DirectoryExists(_hostDirectory).Returns(true);
        _fileSystem.GetFiles(_hostDirectory).Returns([firstCache, others[0], secondCache, others[1]]);

        await _invalidator.OnWorkloadsChangedAsync(CancellationToken.None);

        _fileSystem.Received(1).DeleteFile(firstCache);
        _fileSystem.Received(1).DeleteFile(secondCache);
        _fileSystem.DidNotReceive().DeleteFile(others[0]);
        _fileSystem.DidNotReceive().DeleteFile(others[1]);
    }

    [Fact]
    public async Task OnWorkloadsChangedAsync_DeleteFails_ContinuesAndLogsWarning()
    {
        string lockedCache = Path.Combine(_hostDirectory, "1.0.0", "templatecache.json");
        string otherCache = Path.Combine(_hostDirectory, "2.0.0", "templatecache.json");
        _fileSystem.DirectoryExists(_hostDirectory).Returns(true);
        _fileSystem.GetFiles(_hostDirectory).Returns([lockedCache, otherCache]);
        _fileSystem.When(fileSystem => fileSystem.DeleteFile(lockedCache)).Do(_ => throw new IOException("locked"));

        await _invalidator.OnWorkloadsChangedAsync(CancellationToken.None);

        _fileSystem.Received(1).DeleteFile(otherCache);
        WarningCount().Should().Be(1);
    }

    [Fact]
    public async Task OnWorkloadsChangedAsync_EnumerationFails_LogsWarningWithoutThrowing()
    {
        _fileSystem.DirectoryExists(_hostDirectory).Returns(true);
        _fileSystem.GetFiles(_hostDirectory).Returns(_ => throw new UnauthorizedAccessException("denied"));

        await _invalidator.OnWorkloadsChangedAsync(CancellationToken.None);

        _fileSystem.DidNotReceiveWithAnyArgs().DeleteFile(default!);
        WarningCount().Should().Be(1);
    }

    [Fact]
    public async Task OnWorkloadsChangedAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => _invalidator.OnWorkloadsChangedAsync(cancellation.Token))
            .Should().ThrowExactlyAsync<OperationCanceledException>();
        _fileSystem.DidNotReceiveWithAnyArgs().DeleteFile(default!);
    }

    private int WarningCount()
        => _logger.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning);
}
