// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class FuncTemplateHiveLockTests : IDisposable
{
    private readonly string _root;

    public FuncTemplateHiveLockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ThrowsForNullPaths()
    {
        FluentActions.Invoking(() => new FuncTemplateHiveLock(null!, NullLogger<FuncTemplateHiveLock>.Instance))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsForNullLogger()
    {
        FluentActions.Invoking(() => new FuncTemplateHiveLock(CreatePaths(), null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AcquireAsync_SecondCallerInSameProcess_WaitsUntilFirstReleases()
    {
        var hiveLock = new FuncTemplateHiveLock(CreatePaths(), NullLogger<FuncTemplateHiveLock>.Instance);

        IDisposable first = await hiveLock.AcquireAsync(CancellationToken.None);
        Task<IDisposable> secondTask = hiveLock.AcquireAsync(CancellationToken.None);

        Task completed = await Task.WhenAny(secondTask, Task.Delay(250, CancellationToken.None));
        completed.Should().NotBeSameAs(secondTask, "the hive lock is exclusive within the process");

        first.Dispose();

        IDisposable second = await secondTask;
        second.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_AcrossInstances_SerializesViaFileLock()
    {
        // Separate instances have separate in-process gates, so exclusivity here
        // proves the cross-process file lock, which is what SP-3 requires.
        FuncTemplateEnginePaths paths = CreatePaths();
        var lockA = new FuncTemplateHiveLock(paths, NullLogger<FuncTemplateHiveLock>.Instance);
        var lockB = new FuncTemplateHiveLock(paths, NullLogger<FuncTemplateHiveLock>.Instance);

        IDisposable a = await lockA.AcquireAsync(CancellationToken.None);
        Task<IDisposable> bTask = lockB.AcquireAsync(CancellationToken.None);

        Task completed = await Task.WhenAny(bTask, Task.Delay(300, CancellationToken.None));
        completed.Should().NotBeSameAs(bTask, "the file lock is exclusive across instances/processes");

        a.Dispose();

        IDisposable b = await bTask;
        b.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_WhenHeldByAnotherInstance_HonorsCancellation()
    {
        FuncTemplateEnginePaths paths = CreatePaths();
        var lockA = new FuncTemplateHiveLock(paths, NullLogger<FuncTemplateHiveLock>.Instance);
        var lockB = new FuncTemplateHiveLock(paths, NullLogger<FuncTemplateHiveLock>.Instance);

        using IDisposable a = await lockA.AcquireAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        Task<IDisposable> bTask = lockB.AcquireAsync(cts.Token);

        await cts.CancelAsync();

        await FluentActions.Awaiting(() => bTask).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AcquireAsync_AfterRelease_CanBeReacquired()
    {
        var hiveLock = new FuncTemplateHiveLock(CreatePaths(), NullLogger<FuncTemplateHiveLock>.Instance);

        (await hiveLock.AcquireAsync(CancellationToken.None)).Dispose();
        IDisposable second = await hiveLock.AcquireAsync(CancellationToken.None);

        second.Should().NotBeNull();
        second.Dispose();
    }

    private FuncTemplateEnginePaths CreatePaths()
    {
        string funcHome = Path.Combine(_root, Guid.NewGuid().ToString("N"), "func-home");
        string userProfile = Path.Combine(_root, Guid.NewGuid().ToString("N"), "user-profile");
        return new FuncTemplateEnginePaths(funcHome, userProfile, "1.0.0-test");
    }
}
