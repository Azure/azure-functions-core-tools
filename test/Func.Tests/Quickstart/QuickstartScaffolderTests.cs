// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Quickstart;

public class QuickstartScaffolderTests : IDisposable
{
    private readonly string _targetDir;
    private readonly FakeGitRunner _gitRunner;
    private readonly QuickstartScaffolder _scaffolder;

    public QuickstartScaffolderTests()
    {
        _targetDir = Path.Combine(Path.GetTempPath(), $"func-scaffold-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_targetDir);

        _gitRunner = new FakeGitRunner(
            onRun: SimulateGitCommand,
            onRunWithOutput: args => args.Contains("cat-file") ? "tag" : string.Empty);
        ITemplateFetcher gitFetcher = new GitTemplateFetcher(_gitRunner, NullLogger<GitTemplateFetcher>.Instance);
        ITemplateFetcher httpFetcher = new HttpTemplateFetcher(
            Substitute.For<IHttpClientFactory>(),
            NullLogger<HttpTemplateFetcher>.Instance);
        IFetchModeResolver resolver = new FetchModeResolver(_gitRunner);

        _scaffolder = new QuickstartScaffolder(
            [gitFetcher, httpFetcher],
            resolver,
            NullLogger<QuickstartScaffolder>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_targetDir))
        {
            Directory.Delete(_targetDir, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldAsync_GitMode_VerifiesTagThenClonesThenVerifiesAnnotated()
    {
        QuickstartEntry entry = CreateEntry();

        await _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None);

        Assert.Equal(3, _gitRunner.Calls.Count);

        // Call 1: ls-remote --tags --exit-code
        IReadOnlyList<string> lsRemoteArgs = _gitRunner.Calls[0].Arguments;
        Assert.Contains("ls-remote", lsRemoteArgs);
        Assert.Contains("--tags", lsRemoteArgs);
        Assert.Contains("--exit-code", lsRemoteArgs);
        Assert.Contains($"refs/tags/v1.0.0", lsRemoteArgs);

        // Call 2: clone --depth 1 --branch
        IReadOnlyList<string> cloneArgs = _gitRunner.Calls[1].Arguments;
        Assert.Contains("clone", cloneArgs);
        Assert.Contains("--depth", cloneArgs);
        Assert.Contains("1", cloneArgs);
        Assert.Contains("--branch", cloneArgs);
        Assert.Contains("v1.0.0", cloneArgs);
        Assert.Contains("--", cloneArgs);

        // Call 3: cat-file -t refs/tags/v1.0.0
        IReadOnlyList<string> catFileArgs = _gitRunner.Calls[2].Arguments;
        Assert.Contains("cat-file", catFileArgs);
        Assert.Contains("-t", catFileArgs);
        Assert.Contains("refs/tags/v1.0.0", catFileArgs);
    }

    [Fact]
    public async Task ScaffoldAsync_BranchRef_ThrowsInvalidOperationException()
    {
        // ls-remote --exit-code returns exit code 2 for branch refs (no matching tag)
        var branchRunner = new FakeGitRunner(onRun: (args, _) =>
        {
            if (args.Contains("ls-remote"))
            {
                throw new GitRunnerException(2, "", "", "ls-remote --tags --exit-code");
            }
        });
        ITemplateFetcher gitFetcher = new GitTemplateFetcher(branchRunner, NullLogger<GitTemplateFetcher>.Instance);
        IFetchModeResolver resolver = new FetchModeResolver(branchRunner);
        var scaffolder = new QuickstartScaffolder(
            [gitFetcher],
            resolver,
            NullLogger<QuickstartScaffolder>.Instance);
        QuickstartEntry entry = CreateEntry(gitRef: "main");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
        Assert.Contains("not a tag", ex.Message);
        Assert.Contains("Branch refs", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_NullGitRef_ThrowsArgumentException()
    {
        QuickstartEntry entry = CreateEntry(gitRef: null);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
        Assert.Contains("no GitRef", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_WhitespaceGitRef_ThrowsArgumentException()
    {
        QuickstartEntry entry = CreateEntry(gitRef: "  ");

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
        Assert.Contains("no GitRef", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_LightweightTag_ThrowsInvalidOperationException()
    {
        // cat-file -t returns "commit" for lightweight tags
        var lightweightRunner = new FakeGitRunner(
            onRun: SimulateGitCommand,
            onRunWithOutput: args => args.Contains("cat-file") ? "commit" : string.Empty);
        ITemplateFetcher gitFetcher = new GitTemplateFetcher(lightweightRunner, NullLogger<GitTemplateFetcher>.Instance);
        IFetchModeResolver resolver = new FetchModeResolver(lightweightRunner);
        var scaffolder = new QuickstartScaffolder(
            [gitFetcher],
            resolver,
            NullLogger<QuickstartScaffolder>.Instance);
        QuickstartEntry entry = CreateEntry();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
        Assert.Contains("lightweight tag", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_GitMode_RemovesGitAndGithubDirs()
    {
        QuickstartEntry entry = CreateEntry();

        await _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(_targetDir, ".git")));
        Assert.False(Directory.Exists(Path.Combine(_targetDir, ".github")));
    }

    [Fact]
    public async Task ScaffoldAsync_GitMode_CopiesFilesToTarget()
    {
        QuickstartEntry entry = CreateEntry();

        await _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_targetDir, "host.json")));
        Assert.True(File.Exists(Path.Combine(_targetDir, "function_app.py")));
    }

    [Fact]
    public async Task ScaffoldAsync_WithSubfolder_PromotesSubfolderContents()
    {
        QuickstartEntry entry = CreateEntry(folderPath: "src/starter");

        await _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_targetDir, "starter.txt")));
        Assert.False(File.Exists(Path.Combine(_targetDir, "host.json")));
    }

    [Fact]
    public async Task ScaffoldAsync_AutoMode_GitAvailable_UsesGit()
    {
        QuickstartEntry entry = CreateEntry();

        await _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Auto, CancellationToken.None);

        // ls-remote + clone + cat-file = 3 calls
        Assert.Equal(3, _gitRunner.Calls.Count);
        Assert.Contains("clone", _gitRunner.Calls[1].Arguments);
    }

    [Fact]
    public async Task ScaffoldAsync_AutoMode_GitUnavailable_FallsToHttp()
    {
        var noGitRunner = new FakeGitRunner(version: null);
        ITemplateFetcher gitFetcher = new GitTemplateFetcher(noGitRunner, NullLogger<GitTemplateFetcher>.Instance);
        ITemplateFetcher httpFetcher = new HttpTemplateFetcher(
            Substitute.For<IHttpClientFactory>(),
            NullLogger<HttpTemplateFetcher>.Instance);
        IFetchModeResolver resolver = new FetchModeResolver(noGitRunner);
        var scaffolder = new QuickstartScaffolder(
            [gitFetcher, httpFetcher],
            resolver,
            NullLogger<QuickstartScaffolder>.Instance);
        QuickstartEntry entry = CreateEntry();

        // HTTP mode will fail because we didn't set up a real HttpClient,
        // but we can verify it didn't try git
        await Assert.ThrowsAnyAsync<Exception>(
            () => scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Auto, CancellationToken.None));

        Assert.Empty(noGitRunner.Calls);
    }

    [Fact]
    public async Task ScaffoldAsync_GitRefStartsWithDash_ThrowsArgumentException()
    {
        QuickstartEntry entry = CreateEntry(gitRef: "--upload-pack=evil");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
    }

    [Theory]
    [InlineData(@"C:\evil\path")]
    [InlineData("/etc/passwd")]
    public async Task ScaffoldAsync_RootedFolderPath_ThrowsArgumentException(string folderPath)
    {
        QuickstartEntry entry = CreateEntry(folderPath: folderPath);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
        Assert.Contains("relative path", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_NonGitHubRepo_HttpMode_ThrowsInvalidOperationException()
    {
        var noGitRunner = new FakeGitRunner(version: null);
        ITemplateFetcher gitFetcher = new GitTemplateFetcher(noGitRunner, NullLogger<GitTemplateFetcher>.Instance);
        ITemplateFetcher httpFetcher = new HttpTemplateFetcher(
            Substitute.For<IHttpClientFactory>(),
            NullLogger<HttpTemplateFetcher>.Instance);
        IFetchModeResolver resolver = new FetchModeResolver(noGitRunner);
        var scaffolder = new QuickstartScaffolder(
            [gitFetcher, httpFetcher],
            resolver,
            NullLogger<QuickstartScaffolder>.Instance);
        QuickstartEntry entry = CreateEntry(repositoryUrl: "https://gitlab.com/org/repo");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Http, CancellationToken.None));
        Assert.Contains("github.com", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_FolderPathWithTraversal_ThrowsArgumentException()
    {
        QuickstartEntry entry = CreateEntry(folderPath: "../etc/passwd");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
    }

    [Fact]
    public async Task ScaffoldAsync_NullEntry_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _scaffolder.ScaffoldAsync(null!, _targetDir, FetchMode.Git, CancellationToken.None));
    }

    [Fact]
    public async Task ScaffoldAsync_MissingSubfolder_ThrowsDirectoryNotFoundException()
    {
        QuickstartEntry entry = CreateEntry(folderPath: "nonexistent/path");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
    }

    [Fact]
    public async Task ScaffoldAsync_NullTargetDirectory_ThrowsArgumentNullException()
    {
        QuickstartEntry entry = CreateEntry();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _scaffolder.ScaffoldAsync(entry, null!, FetchMode.Git, CancellationToken.None));
    }

    [Fact]
    public async Task ScaffoldAsync_WhitespaceTargetDirectory_ThrowsArgumentException()
    {
        QuickstartEntry entry = CreateEntry();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _scaffolder.ScaffoldAsync(entry, "   ", FetchMode.Git, CancellationToken.None));
    }

    [Fact]
    public async Task ScaffoldAsync_GitRunnerThrows_PropagatesAsInvalidOperationException()
    {
        var failingRunner = new FakeGitRunner(exception: new GitRunnerException(2, "fatal: repo not found", "", "ls-remote"));
        ITemplateFetcher gitFetcher = new GitTemplateFetcher(failingRunner, NullLogger<GitTemplateFetcher>.Instance);
        IFetchModeResolver resolver = new FetchModeResolver(failingRunner);
        var scaffolder = new QuickstartScaffolder(
            [gitFetcher],
            resolver,
            NullLogger<QuickstartScaffolder>.Instance);
        QuickstartEntry entry = CreateEntry();

        // ls-remote fails → wrapped as InvalidOperationException by GitTemplateFetcher
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
        Assert.IsType<GitRunnerException>(ex.InnerException);
    }

    [Fact]
    public async Task ScaffoldAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _scaffolder.ScaffoldAsync(CreateEntry(), _targetDir, FetchMode.Git, cts.Token));
    }

    [Fact]
    public async Task ScaffoldAsync_FetcherThrows_CleansTempDirectory()
    {
        string[] tempDirsBefore = Directory.GetDirectories(Path.GetTempPath(), "func-quickstart-*");

        var failingRunner = new FakeGitRunner(exception: new GitRunnerException(2, "fatal: repo not found", "", "ls-remote"));
        ITemplateFetcher gitFetcher = new GitTemplateFetcher(failingRunner, NullLogger<GitTemplateFetcher>.Instance);
        IFetchModeResolver resolver = new FetchModeResolver(failingRunner);
        var scaffolder = new QuickstartScaffolder(
            [gitFetcher],
            resolver,
            NullLogger<QuickstartScaffolder>.Instance);
        QuickstartEntry entry = CreateEntry();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));

        string[] tempDirsAfter = Directory.GetDirectories(Path.GetTempPath(), "func-quickstart-*");
        Assert.Equal(tempDirsBefore.Length, tempDirsAfter.Length);
    }

    /// <summary>
    /// Simulates git commands: ls-remote (no-op), clone (creates files), cat-file (no-op).
    /// </summary>
    private static void SimulateGitCommand(IReadOnlyList<string> args, string? workingDirectory)
    {
        // ls-remote and cat-file succeed as no-ops — only clone needs to create files
        if (!args.Contains("clone"))
        {
            return;
        }

        // Last argument is the target path
        string cloneTarget = args[^1];
        Directory.CreateDirectory(cloneTarget);

        // Simulate .git and .github dirs
        Directory.CreateDirectory(Path.Combine(cloneTarget, ".git"));
        File.WriteAllText(Path.Combine(cloneTarget, ".git", "HEAD"), "ref: refs/heads/main");
        Directory.CreateDirectory(Path.Combine(cloneTarget, ".github"));
        File.WriteAllText(Path.Combine(cloneTarget, ".github", "CODEOWNERS"), "* @team");

        // Simulate template files
        File.WriteAllText(Path.Combine(cloneTarget, "host.json"), "{}");
        File.WriteAllText(Path.Combine(cloneTarget, "function_app.py"), "import azure.functions");

        // Simulate subfolder for subfolder promotion tests
        string subDir = Path.Combine(cloneTarget, "src", "starter");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "starter.txt"), "starter content");
    }

    private static QuickstartEntry CreateEntry(
        string? gitRef = "v1.0.0",
        string folderPath = ".",
        string repositoryUrl = "https://github.com/Azure-Samples/functions-quickstart")
    {
        return new QuickstartEntry(
            Id: "test-template",
            DisplayName: "Test Template",
            Language: "Python",
            Resource: "http",
            Iac: "bicep",
            RepositoryUrl: repositoryUrl,
            FolderPath: folderPath,
            GitRef: gitRef,
            ShortDescription: "A test template",
            LongDescription: "A test template for unit tests",
            WhatsIncluded: ["HTTP trigger", "Bicep files"],
            Priority: 1);
    }
}
