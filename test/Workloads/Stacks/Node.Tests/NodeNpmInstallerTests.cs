// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public sealed class NodeNpmInstallerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateStartInfo_UsesOnlyFixedWorkloadCommand(bool windows)
    {
        ProcessStartInfo info = NodeNpmInstaller.CreateStartInfo("a project with spaces", windows);

        info.FileName.Should().Be(windows ? "cmd.exe" : "npm");
        info.ArgumentList.Should().Equal(windows ? ["/c", "npm", "install"] : ["install"]);
        info.Arguments.Should().BeEmpty();
        info.WorkingDirectory.Should().Be("a project with spaces");
        info.RedirectStandardOutput.Should().BeTrue();
        info.RedirectStandardError.Should().BeTrue();
        info.UseShellExecute.Should().BeFalse();
        info.CreateNoWindow.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task InstallRequiredAsync_DrainsBothStreamsBeforeWaitingAndPreservesOutcome(int exitCode)
    {
        using var child = new ControlledProcess(exitCode);
        var installer = new ControlledInstaller(child);

        (int ExitCode, string Stderr) result = await installer.InstallRequiredAsync("project", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().Be((exitCode, "owned stderr"));
        installer.Directory.Should().Be("project");
        child.StandardOutputRead.Should().BeTrue();
        child.StandardErrorRead.Should().BeTrue();
        child.WaitCalls.Should().Be(1);
        child.KillCalls.Should().Be(0);
        child.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstallRequiredAsync_Cancellation_KillsOnlyOwnedChildDrainsAndPropagates(bool alreadyExited)
    {
        using var cancellation = new CancellationTokenSource();
        using var child = new ControlledProcess(0, cancellation, alreadyExited);
        var installer = new ControlledInstaller(child);

        Func<Task> install = () => installer.InstallRequiredAsync("project", cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));

        await install.Should().ThrowAsync<OperationCanceledException>();
        child.KillCalls.Should().Be(alreadyExited ? 0 : 1);
        child.WaitCalls.Should().Be(2);
        child.Disposed.Should().BeTrue();
        child.StandardOutputRead.Should().BeTrue();
        child.StandardErrorRead.Should().BeTrue();
    }

    [Fact]
    public async Task InstallRequiredAsync_AlreadyCancelled_DoesNotStartProcess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var child = new ControlledProcess(0);
        var installer = new ControlledInstaller(child);

        Func<Task> install = () => installer.InstallRequiredAsync("project", cancellation.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        installer.Directory.Should().BeNull();
        child.WaitCalls.Should().Be(0);
    }

    [Fact]
    public async Task InstallRequiredAsync_MissingExecutable_ReturnsExplicitFailure()
    {
        var installer = new MissingExecutableInstaller();

        (int ExitCode, string Stderr) result = await installer.InstallRequiredAsync("project", CancellationToken.None);

        result.Should().Be((-1, "owned npm executable missing"));
    }

    [Fact]
    public async Task InstallRequiredAsync_UnexpectedBoundaryFailure_Propagates()
    {
        var installer = new UnexpectedFailureInstaller();

        Func<Task> install = () => installer.InstallRequiredAsync("project", CancellationToken.None);

        await install.Should().ThrowAsync<InvalidOperationException>().WithMessage("unexpected owned process failure");
    }

    [Fact]
    public async Task InstallAsync_LegacyBestEffort_UnexpectedFailureRemainsFailureResult()
    {
        var installer = new UnexpectedFailureInstaller();

        (int ExitCode, string Stderr) result = await installer.InstallAsync("project", CancellationToken.None);

        result.Should().Be((-1, "unexpected owned process failure"));
    }

    [Fact]
    public async Task InstallAsync_LegacyBestEffort_CancellationRemainsFailureResult()
    {
        using var cancellation = new CancellationTokenSource();
        using var child = new ControlledProcess(0, cancellation);
        var installer = new ControlledInstaller(child);

        (int ExitCode, string Stderr) result = await installer.InstallAsync("project", cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));

        result.ExitCode.Should().Be(-1);
        result.Stderr.Should().NotBeEmpty();
        child.KillCalls.Should().Be(1);
        child.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InstallRequiredAsync_InvalidDirectory_RejectsWithoutProcess(string? directory)
    {
        using var child = new ControlledProcess(0);
        var installer = new ControlledInstaller(child);

        Func<Task> install = () => installer.InstallRequiredAsync(directory!, CancellationToken.None);

        await install.Should().ThrowAsync<ArgumentException>();
        installer.Directory.Should().BeNull();
    }

    private sealed class ControlledInstaller(NodeNpmProcess child) : NodeNpmInstaller
    {
        public string? Directory { get; private set; }
        internal override NodeNpmProcess StartProcess(string workingDirectory)
        {
            Directory = workingDirectory;
            return child;
        }
    }

    private sealed class MissingExecutableInstaller : NodeNpmInstaller
    {
        internal override NodeNpmProcess StartProcess(string workingDirectory)
            => throw new Win32Exception("owned npm executable missing");
    }

    private sealed class UnexpectedFailureInstaller : NodeNpmInstaller
    {
        internal override NodeNpmProcess StartProcess(string workingDirectory)
            => throw new InvalidOperationException("unexpected owned process failure");
    }

    private sealed class ControlledProcess : NodeNpmProcess
    {
        private readonly TaskCompletionSource<string> _stdout = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _stderr = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource? _cancellation;
        private readonly bool _exitBeforeCancellation;
        private bool _exited;
        public bool StandardOutputRead { get; private set; }
        public bool StandardErrorRead { get; private set; }
        public bool Disposed { get; private set; }
        public int WaitCalls { get; private set; }
        public int KillCalls { get; private set; }
        public override TextReader StandardOutput { get; }
        public override TextReader StandardError { get; }
        public override bool HasExited => _exited;
        public override int ExitCode { get; }

        public ControlledProcess(int exitCode, CancellationTokenSource? cancellation = null, bool exitBeforeCancellation = false)
        {
            ExitCode = exitCode;
            _cancellation = cancellation;
            _exitBeforeCancellation = exitBeforeCancellation;
            StandardOutput = Substitute.For<TextReader>();
            StandardError = Substitute.For<TextReader>();
            StandardOutput.ReadToEndAsync(CancellationToken.None).Returns(_ =>
            {
                StandardOutputRead = true;
                return _stdout.Task;
            });
            StandardError.ReadToEndAsync(CancellationToken.None).Returns(_ =>
            {
                StandardErrorRead = true;
                return _stderr.Task;
            });
        }

        public override Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitCalls++;
            StandardOutputRead.Should().BeTrue("both streams must drain before the child can exit");
            StandardErrorRead.Should().BeTrue("both streams must drain before the child can exit");
            if (!_exited && _cancellation is not null)
            {
                _exited = _exitBeforeCancellation;
                if (_exited)
                {
                    CompleteStreams();
                }
                _cancellation.Cancel();
                return Task.FromCanceled(cancellationToken);
            }
            _exited = true;
            CompleteStreams();
            return Task.CompletedTask;
        }

        public override void Kill()
        {
            KillCalls++;
            _exited = true;
            CompleteStreams();
        }

        public override void Dispose() => Disposed = true;

        private void CompleteStreams()
        {
            _stdout.TrySetResult("owned stdout");
            _stderr.TrySetResult("owned stderr");
        }
    }
}
