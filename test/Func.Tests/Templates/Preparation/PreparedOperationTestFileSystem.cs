// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Preparation;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Preparation;

internal sealed class PreparedOperationTestFileSystem
{
    private readonly Dictionary<string, FileAttributes> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private int _nextStage;

    public PreparedOperationTestFileSystem()
    {
        string? directory = Target;
        while (directory is not null)
        {
            SetDirectory(directory);
            directory = Path.GetDirectoryName(directory);
        }

        FileSystem.GetAttributesIfExists(Arg.Any<string>()).Returns(call =>
            _attributes.TryGetValue(call.Arg<string>(), out FileAttributes attributes) ? attributes : (FileAttributes?)null);
        FileSystem.CreateStagingDirectory(Arg.Any<string>()).Returns(call =>
        {
            string parent = call.Arg<string>();
            if (!_attributes.TryGetValue(parent, out FileAttributes attributes) || attributes != FileAttributes.Directory)
            {
                throw new DirectoryNotFoundException(parent);
            }

            string path = Path.Combine(parent, "stage-" + ++_nextStage);
            RequireAbsent(path);
            SetDirectory(path);
            Stages.Add(path);
            return path;
        });
        FileSystem.CreateNewFile(Arg.Any<string>()).Returns(call =>
        {
            string path = call.Arg<string>();
            RequireAbsent(path);
            _attributes[path] = FileAttributes.Normal;
            if (StagingStreamFactory is not null)
            {
                return StagingStreamFactory(path);
            }

            return new CapturingStream(
                bytes =>
                {
                    _files[path] = bytes;
                    AfterStagingWrite?.Invoke(path);
                },
                token => BeforeWriteAsync?.Invoke(token) ?? Task.CompletedTask);
        });
        FileSystem.When(fileSystem => fileSystem.PublishFileNoReplace(Arg.Any<string>(), Arg.Any<string>())).Do(call =>
        {
            string source = call.ArgAt<string>(0);
            string destination = call.ArgAt<string>(1);
            BeforePublishFile?.Invoke(destination);
            RequireAbsent(destination);
            _files[destination] = _files[source];
            _attributes[destination] = FileAttributes.Normal;
            AfterPublishFile?.Invoke(destination);
        });
        FileSystem.When(fileSystem => fileSystem.CreateNewDirectory(Arg.Any<string>())).Do(call =>
        {
            string destination = call.Arg<string>();
            BeforeCreateDirectory?.Invoke(destination);
            RequireAbsent(destination);
            SetDirectory(destination);
            AfterCreateDirectory?.Invoke(destination);
        });
        FileSystem.When(fileSystem => fileSystem.DeleteDirectory(Arg.Any<string>())).Do(call =>
        {
            string path = call.Arg<string>();
            CleanupAttempts.Add(path);
            if (DeleteFailure?.Invoke(path) is { } failure)
            {
                throw failure;
            }

            foreach (string child in _attributes.Keys.Where(candidate => candidate == path
                || candidate.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _attributes.Remove(child);
                _files.Remove(child);
            }
        });
        FileSystem.ClearReceivedCalls();
    }

    public static string Target => Path.DirectorySeparatorChar == '\\' ? @"C:\prepared" : "/prepared";

    public static PreparedOperationIdentity Identity => new("Example.Templates", "1.0.0", "Example.Http");

    public IFileSystem FileSystem { get; } = Substitute.For<IFileSystem>();

    public IReadOnlyDictionary<string, byte[]> Files => _files;

    public List<string> Stages { get; } = [];

    public List<string> CleanupAttempts { get; } = [];

    public Action<string>? BeforePublishFile { get; set; }

    public Action<string>? AfterPublishFile { get; set; }

    public Action<string>? BeforeCreateDirectory { get; set; }

    public Action<string>? AfterCreateDirectory { get; set; }

    public Action<string>? AfterStagingWrite { get; set; }

    public Func<CancellationToken, Task>? BeforeWriteAsync { get; set; }

    public Func<string, Stream>? StagingStreamFactory { get; set; }

    public Func<string, Exception?>? DeleteFailure { get; set; }

    public static PreparedOperationPlan Plan(PreparedOutputEntry first, PreparedOutputEntry? second = null)
        => new(Target, Identity, second is null ? [first] : [first, second]);

    public static string Destination(string relativePath) => Path.Combine(Target, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void SetDirectory(string path) => _attributes[path] = FileAttributes.Directory;

    public void SetFile(string path, params byte[] content)
    {
        _attributes[path] = FileAttributes.Normal;
        _files[path] = content;
    }

    public void SetLink(string path) => _attributes[path] = FileAttributes.Directory | FileAttributes.ReparsePoint;

    public void RemovePath(string path)
    {
        _attributes.Remove(path);
        _files.Remove(path);
    }

    private void RequireAbsent(string path)
    {
        if (_attributes.ContainsKey(path))
        {
            throw new IOException("Destination already exists: " + path);
        }
    }

    private sealed class CapturingStream(Action<byte[]> completed, Func<CancellationToken, Task> beforeWrite) : MemoryStream
    {
        private bool _closed;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await beforeWrite(cancellationToken);
            await base.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                completed(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
