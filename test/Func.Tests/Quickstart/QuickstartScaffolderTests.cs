// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Text;
using Azure.Functions.Cli.Quickstart;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Quickstart;

public sealed class QuickstartScaffolderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IGitRunner _gitRunner = Substitute.For<IGitRunner>();

    public QuickstartScaffolderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-quickstart-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            // .git/objects/pack/*.idx may be marked read-only by git on Windows.
            MakeWritable(_tempDir);
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // --- Argument validation ------------------------------------------------

    [Fact]
    public async Task ScaffoldAsync_NullEntry_Throws()
    {
        var scaffolder = CreateScaffolder();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            scaffolder.ScaffoldAsync(null!, _tempDir, FetchMode.Http, CancellationToken.None));
    }

    [Fact]
    public async Task ScaffoldAsync_NullTargetPath_Throws()
    {
        var scaffolder = CreateScaffolder();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            scaffolder.ScaffoldAsync(ValidEntry(), null!, FetchMode.Http, CancellationToken.None));
    }

    [Fact]
    public async Task ScaffoldAsync_EmptyTargetPath_Throws()
    {
        var scaffolder = CreateScaffolder();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scaffolder.ScaffoldAsync(ValidEntry(), "", FetchMode.Http, CancellationToken.None));
    }

    // --- Repo URL allow-list -------------------------------------------------

    [Fact]
    public async Task ScaffoldAsync_DisallowedRepoUrl_ThrowsInvalidOperation()
    {
        var scaffolder = CreateScaffolder();
        var entry = ValidEntry() with { RepositoryUrl = "https://github.com/attacker/repo" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scaffolder.ScaffoldAsync(entry, _tempDir, FetchMode.Http, CancellationToken.None));

        Assert.Contains("not allowed", ex.Message);
    }

    // --- Target-path empty-check --------------------------------------------

    [Fact]
    public async Task ScaffoldAsync_NonEmptyTargetDir_ThrowsInvalidOperation()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "existing.txt"), "x");

        var scaffolder = CreateScaffolder();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scaffolder.ScaffoldAsync(ValidEntry(), _tempDir, FetchMode.Http, CancellationToken.None));

        Assert.Contains("already exists and is not empty", ex.Message);
    }

    // --- FetchMode resolution -----------------------------------------------

    [Fact]
    public async Task ScaffoldAsync_GitModeButGitMissing_ThrowsInvalidOperation()
    {
        _gitRunner.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);

        var scaffolder = CreateScaffolder();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scaffolder.ScaffoldAsync(ValidEntry(), _tempDir, FetchMode.Git, CancellationToken.None));

        Assert.Contains("git 2.25 or later was not found", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_AutoMode_PrefersGitWhenAvailable()
    {
        _gitRunner.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        _gitRunner.ShallowCloneAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                // Simulate a successful clone by creating a file inside target.
                string target = (string)call[2];
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "README.md"), "hello");
                return new GitCloneResult(0, string.Empty);
            });

        var scaffolder = CreateScaffolder();
        await scaffolder.ScaffoldAsync(ValidEntry(), _tempDir, FetchMode.Auto, CancellationToken.None);

        await _gitRunner.Received(1).ShallowCloneAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScaffoldAsync_AutoMode_FallsBackToHttpWhenGitMissing()
    {
        _gitRunner.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);

        var scaffolder = CreateScaffolderWithZip(BuildZip([("README.md", "hi")]));
        await scaffolder.ScaffoldAsync(ValidEntry(), _tempDir, FetchMode.Auto, CancellationToken.None);

        await _gitRunner.DidNotReceive().ShallowCloneAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.True(File.Exists(Path.Combine(_tempDir, "README.md")));
    }

    // --- Git failure propagation --------------------------------------------

    [Fact]
    public async Task ScaffoldAsync_GitMode_NonZeroExit_ThrowsWithStderr()
    {
        _gitRunner.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        _gitRunner.ShallowCloneAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GitCloneResult(128, "fatal: repository not found"));

        var scaffolder = CreateScaffolder();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scaffolder.ScaffoldAsync(ValidEntry(), _tempDir, FetchMode.Git, CancellationToken.None));

        Assert.Contains("git clone failed", ex.Message);
        Assert.Contains("repository not found", ex.Message);
    }

    // --- Http extraction ----------------------------------------------------

    [Fact]
    public async Task ScaffoldAsync_HttpMode_ExtractsZipFiles()
    {
        var scaffolder = CreateScaffolderWithZip(BuildZip([
            ("README.md", "hello"),
            ("src/app.py", "print('hi')"),
        ]));

        await scaffolder.ScaffoldAsync(ValidEntry(), _tempDir, FetchMode.Http, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_tempDir, "README.md")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "src", "app.py")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_tempDir, "README.md")));
    }

    [Fact]
    public async Task ScaffoldAsync_HttpMode_EmptyArchive_ThrowsInvalidOperation()
    {
        // An archive with only the top-level dir and no files should surface as
        // a real failure rather than leaving the user with an empty directory.
        var scaffolder = CreateScaffolderWithZip(BuildZip([]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scaffolder.ScaffoldAsync(ValidEntry(), _tempDir, FetchMode.Http, CancellationToken.None));

        Assert.Contains("no extractable files", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_HttpMode_FolderPathFilter_ExtractsOnlyMatchingSubfolder()
    {
        var scaffolder = CreateScaffolderWithZip(BuildZip([
            ("samples/myapp/README.md", "hi"),
            ("samples/myapp/code.py", "x"),
            ("samples/other/skip.md", "no"),
            ("docs/notes.md", "no"),
        ]));
        var entry = ValidEntry() with { FolderPath = "samples/myapp" };

        await scaffolder.ScaffoldAsync(entry, _tempDir, FetchMode.Http, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_tempDir, "README.md")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "code.py")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "skip.md")));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "samples")));
    }

    [Fact]
    public async Task ScaffoldAsync_HttpMode_PathTraversalInZip_Throws()
    {
        // Manually craft an archive whose entry escapes the top-level prefix.
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Top-level directory entry (matches what GitHub produces).
            archive.CreateEntry("repo-HEAD/");
            // Malicious entry attempts to write outside the target.
            var bad = archive.CreateEntry("repo-HEAD/../../escape.txt");
            using var w = new StreamWriter(bad.Open());
            w.Write("evil");
        }

        ms.Position = 0;
        var scaffolder = CreateScaffolderWithZip(ms.ToArray());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scaffolder.ScaffoldAsync(ValidEntry(), _tempDir, FetchMode.Http, CancellationToken.None));

        Assert.Contains("outside the target directory", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_HttpMode_RemovesGitMetadataAfterFetch()
    {
        var scaffolder = CreateScaffolderWithZip(BuildZip([
            ("README.md", "hi"),
            (".git/HEAD", "ref: main"),
            (".github/workflows/ci.yml", "name: ci"),
        ]));

        await scaffolder.ScaffoldAsync(ValidEntry(), _tempDir, FetchMode.Http, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_tempDir, "README.md")));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, ".git")));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, ".github")));
    }

    // --- Helpers ------------------------------------------------------------

    private QuickstartScaffolder CreateScaffolder() =>
        new(new HttpClient(), _gitRunner, NullLogger<QuickstartScaffolder>.Instance);

    private TestScaffolder CreateScaffolderWithZip(byte[] zipBytes) =>
        new(new HttpClient(), _gitRunner, zipBytes);

    private static QuickstartEntry ValidEntry() =>
        new()
        {
            Id = "test",
            DisplayName = "Test Template",
            Language = "Python",
            Resource = "HTTP Trigger",
            RepositoryUrl = "https://github.com/Azure/test-repo",
            FolderPath = ".",
        };

    /// <summary>
    /// Builds a GitHub-style archive zip whose top-level directory is
    /// <c>repo-HEAD/</c> (matching <see cref="QuickstartScaffolder"/>'s
    /// prefix-detection logic).
    /// </summary>
    private static byte[] BuildZip(IEnumerable<(string Path, string Content)> entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Top-level directory entry (first entry, as GitHub archives produce).
            archive.CreateEntry("test-repo-HEAD/");

            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry($"test-repo-HEAD/{path}");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }

    private static void MakeWritable(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }
    }

    /// <summary>
    /// Test-only scaffolder that overrides the HTTP download seam to return a
    /// pre-built zip stream, so tests don't depend on the network.
    /// </summary>
    private sealed class TestScaffolder : QuickstartScaffolder
    {
        private readonly byte[] _zipBytes;

        public TestScaffolder(HttpClient httpClient, IGitRunner gitRunner, byte[] zipBytes)
            : base(httpClient, gitRunner, NullLogger<QuickstartScaffolder>.Instance)
        {
            _zipBytes = zipBytes;
        }

        protected override Task<Stream> DownloadZipAsync(Uri zipUrl, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(_zipBytes));
    }
}
