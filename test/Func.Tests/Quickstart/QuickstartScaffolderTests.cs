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

        _gitRunner = new FakeGitRunner(onRun: SimulateGitCommand, onRunWithOutput: SimulateGitOutputCommand);
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
    public async Task ScaffoldAsync_GitMode_InitsFetchesCheckoutsAndVerifiesTag()
    {
        QuickstartEntry entry = CreateEntry();

        await _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None);

        // init → remote add → fetch → checkout → cat-file → rev-parse → rev-list = 7 calls
        Assert.Equal(7, _gitRunner.Calls.Count);

        // Call 1: git init <tempDir>
        Assert.Contains("init", _gitRunner.Calls[0].Arguments);

        // Call 2: git -C <tempDir> remote add origin -- <url>
        IReadOnlyList<string> remoteArgs = _gitRunner.Calls[1].Arguments;
        Assert.Contains("remote", remoteArgs);
        Assert.Contains("add", remoteArgs);
        Assert.Contains("origin", remoteArgs);

        // Call 3: git -C <tempDir> fetch --depth 1 --no-tags origin refs/tags/v1.0.0:refs/tags/v1.0.0
        IReadOnlyList<string> fetchArgs = _gitRunner.Calls[2].Arguments;
        Assert.Contains("fetch", fetchArgs);
        Assert.Contains("--depth", fetchArgs);
        Assert.Contains("--no-tags", fetchArgs);
        Assert.Contains("refs/tags/v1.0.0:refs/tags/v1.0.0", fetchArgs);

        // Call 4: git -C <tempDir> checkout --detach refs/tags/v1.0.0
        IReadOnlyList<string> checkoutArgs = _gitRunner.Calls[3].Arguments;
        Assert.Contains("checkout", checkoutArgs);
        Assert.Contains("--detach", checkoutArgs);
        Assert.Contains("refs/tags/v1.0.0", checkoutArgs);

        // Call 5: git -C <tempDir> cat-file -t refs/tags/v1.0.0
        IReadOnlyList<string> catFileArgs = _gitRunner.Calls[4].Arguments;
        Assert.Contains("cat-file", catFileArgs);
        Assert.Contains("-t", catFileArgs);
        Assert.Contains("refs/tags/v1.0.0", catFileArgs);

        // Call 6: git -C <tempDir> rev-parse HEAD
        IReadOnlyList<string> revParseArgs = _gitRunner.Calls[5].Arguments;
        Assert.Contains("rev-parse", revParseArgs);
        Assert.Contains("HEAD", revParseArgs);

        // Call 7: git -C <tempDir> rev-list -n 1 refs/tags/v1.0.0
        IReadOnlyList<string> revListArgs = _gitRunner.Calls[6].Arguments;
        Assert.Contains("rev-list", revListArgs);
        Assert.Contains("refs/tags/v1.0.0", revListArgs);
    }

    [Fact]
    public async Task ScaffoldAsync_BranchRef_ThrowsArgumentException()
    {
        QuickstartEntry entry = CreateEntry(gitRef: "refs/heads/main");

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
        Assert.Contains("not a tag ref", ex.Message);
    }

    [Fact]
    public async Task ScaffoldAsync_BareTagName_ThrowsArgumentException()
    {
        QuickstartEntry entry = CreateEntry(gitRef: "v1.0.0");

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
        Assert.Contains("not a tag ref", ex.Message);
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
            onRunWithOutput: args => args.Contains("cat-file") ? "commit" : FakeCommitHash);
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

        // init → remote add → fetch → checkout → cat-file → rev-parse → rev-list = 7 calls
        Assert.Equal(7, _gitRunner.Calls.Count);
        Assert.Contains("init", _gitRunner.Calls[0].Arguments);
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
    public async Task ScaffoldAsync_GitRunnerThrows_PropagatesAsGitRunnerException()
    {
        var failingRunner = new FakeGitRunner(exception: new GitRunnerException(128, "fatal: repo not found", "", "init"));
        ITemplateFetcher gitFetcher = new GitTemplateFetcher(failingRunner, NullLogger<GitTemplateFetcher>.Instance);
        IFetchModeResolver resolver = new FetchModeResolver(failingRunner);
        var scaffolder = new QuickstartScaffolder(
            [gitFetcher],
            resolver,
            NullLogger<QuickstartScaffolder>.Instance);
        QuickstartEntry entry = CreateEntry();

        // init fails → GitRunnerException propagates directly (not wrapped)
        await Assert.ThrowsAsync<GitRunnerException>(
            () => scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));
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

        var failingRunner = new FakeGitRunner(exception: new GitRunnerException(128, "fatal: repo not found", "", "init"));
        ITemplateFetcher gitFetcher = new GitTemplateFetcher(failingRunner, NullLogger<GitTemplateFetcher>.Instance);
        IFetchModeResolver resolver = new FetchModeResolver(failingRunner);
        var scaffolder = new QuickstartScaffolder(
            [gitFetcher],
            resolver,
            NullLogger<QuickstartScaffolder>.Instance);
        QuickstartEntry entry = CreateEntry();

        await Assert.ThrowsAsync<GitRunnerException>(
            () => scaffolder.ScaffoldAsync(entry, _targetDir, FetchMode.Git, CancellationToken.None));

        string[] tempDirsAfter = Directory.GetDirectories(Path.GetTempPath(), "func-quickstart-*");
        Assert.Equal(tempDirsBefore.Length, tempDirsAfter.Length);
    }

    private const string FakeCommitHash = "abc123def456";

    /// <summary>
    /// Simulates git commands for the init+fetch+checkout flow.
    /// <c>init</c> creates the directory structure, <c>checkout</c> populates template files.
    /// Other commands (remote add, fetch) succeed as no-ops.
    /// </summary>
    private static void SimulateGitCommand(IReadOnlyList<string> args, string? workingDirectory)
    {
        if (args.Contains("init"))
        {
            // git init <path> — create the directory
            string initTarget = args[^1];
            Directory.CreateDirectory(initTarget);
            Directory.CreateDirectory(Path.Combine(initTarget, ".git"));
            File.WriteAllText(Path.Combine(initTarget, ".git", "HEAD"), "ref: refs/heads/main");
            return;
        }

        if (args.Contains("checkout"))
        {
            // git -C <tempDir> checkout — populate template files
            int dashCIndex = -1;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "-C")
                {
                    dashCIndex = i;
                    break;
                }
            }

            if (dashCIndex < 0 || dashCIndex + 1 >= args.Count)
            {
                return;
            }

            string repoDir = args[dashCIndex + 1];

            // Simulate .github dir
            Directory.CreateDirectory(Path.Combine(repoDir, ".github"));
            File.WriteAllText(Path.Combine(repoDir, ".github", "CODEOWNERS"), "* @team");

            // Simulate template files
            File.WriteAllText(Path.Combine(repoDir, "host.json"), "{}");
            File.WriteAllText(Path.Combine(repoDir, "function_app.py"), "import azure.functions");

            // Simulate subfolder for subfolder promotion tests
            string subDir = Path.Combine(repoDir, "src", "starter");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "starter.txt"), "starter content");
        }
    }

    /// <summary>
    /// Simulates git output commands: <c>cat-file</c> returns "tag",
    /// <c>rev-parse</c> and <c>rev-list</c> return matching commit hashes.
    /// </summary>
    private static string SimulateGitOutputCommand(IReadOnlyList<string> args)
    {
        if (args.Contains("cat-file"))
        {
            return "tag";
        }

        if (args.Contains("rev-parse") || args.Contains("rev-list"))
        {
            return FakeCommitHash;
        }

        return string.Empty;
    }

    private static QuickstartEntry CreateEntry(
        string? gitRef = "refs/tags/v1.0.0",
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

