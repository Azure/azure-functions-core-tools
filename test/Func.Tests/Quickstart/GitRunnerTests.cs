// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;
using Xunit;

namespace Azure.Functions.Cli.Tests.Quickstart;

public class GitRunnerTests
{
    // --- BuildCloneArgs ------------------------------------------------------

    [Fact]
    public void BuildCloneArgs_NullRef_OmitsBranchFlag()
    {
        var args = new List<string>();

        GitRunner.BuildCloneArgs(args, "https://github.com/Azure/repo", gitRef: null, "C:/tmp", sparse: false);

        Assert.DoesNotContain("--branch", args);
        Assert.DoesNotContain("--single-branch", args);
    }

    [Fact]
    public void BuildCloneArgs_EmptyRef_OmitsBranchFlag()
    {
        var args = new List<string>();

        GitRunner.BuildCloneArgs(args, "https://github.com/Azure/repo", gitRef: "", "C:/tmp", sparse: false);

        Assert.DoesNotContain("--branch", args);
    }

    [Fact]
    public void BuildCloneArgs_HeadRef_OmitsBranchFlag()
    {
        // "HEAD" is not a valid value for `git clone --branch`; treat it as
        // "use the remote's default branch" the same way null/empty does.
        var args = new List<string>();

        GitRunner.BuildCloneArgs(args, "https://github.com/Azure/repo", gitRef: "HEAD", "C:/tmp", sparse: false);

        Assert.DoesNotContain("--branch", args);
        Assert.DoesNotContain("HEAD", args);
    }

    [Fact]
    public void BuildCloneArgs_ConcreteRef_IncludesBranchFlag()
    {
        var args = new List<string>();

        GitRunner.BuildCloneArgs(args, "https://github.com/Azure/repo", gitRef: "v1.2.3", "C:/tmp", sparse: false);

        Assert.Contains("--branch", args);
        Assert.Contains("v1.2.3", args);
        Assert.Contains("--single-branch", args);
    }

    [Fact]
    public void BuildCloneArgs_Sparse_IncludesSparseFlag()
    {
        var args = new List<string>();

        GitRunner.BuildCloneArgs(args, "https://github.com/Azure/repo", gitRef: null, "C:/tmp", sparse: true);

        Assert.Contains("--sparse", args);
    }

    [Fact]
    public void BuildCloneArgs_NonSparse_OmitsSparseFlag()
    {
        var args = new List<string>();

        GitRunner.BuildCloneArgs(args, "https://github.com/Azure/repo", gitRef: null, "C:/tmp", sparse: false);

        Assert.DoesNotContain("--sparse", args);
    }

    [Fact]
    public void BuildCloneArgs_AlwaysShallow()
    {
        var args = new List<string>();

        GitRunner.BuildCloneArgs(args, "https://github.com/Azure/repo", gitRef: null, "C:/tmp", sparse: false);

        int depthIndex = args.IndexOf("--depth");
        Assert.True(depthIndex >= 0);
        Assert.Equal("1", args[depthIndex + 1]);
    }

    [Fact]
    public void BuildCloneArgs_UsesEndOfOptionsSentinel()
    {
        // `--` prevents an attacker-controlled URL starting with `-` from
        // being parsed as an option.
        var args = new List<string>();

        GitRunner.BuildCloneArgs(args, "https://github.com/Azure/repo", gitRef: null, "C:/tmp/target", sparse: false);

        int sentinel = args.IndexOf("--");
        Assert.True(sentinel >= 0, "Expected '--' end-of-options sentinel");
        Assert.Equal("https://github.com/Azure/repo", args[sentinel + 1]);
        Assert.Equal("C:/tmp/target", args[sentinel + 2]);
    }

    [Fact]
    public void BuildCloneArgs_DisablesCoreAutoCrlf()
    {
        var args = new List<string>();

        GitRunner.BuildCloneArgs(args, "https://github.com/Azure/repo", gitRef: null, "C:/tmp", sparse: false);

        int idx = args.IndexOf("--config");
        Assert.True(idx >= 0);
        Assert.Equal("core.autocrlf=false", args[idx + 1]);
    }

    // --- BuildSparseSetArgs --------------------------------------------------

    [Fact]
    public void BuildSparseSetArgs_IncludesEndOfOptionsAndFolder()
    {
        var args = new List<string>();

        GitRunner.BuildSparseSetArgs(args, "samples/myapp");

        Assert.Equal("sparse-checkout", args[0]);
        Assert.Equal("set", args[1]);
        Assert.Equal("--", args[2]);
        Assert.Equal("samples/myapp", args[3]);
    }

    // --- ShallowCloneAsync argument validation -------------------------------

    [Fact]
    public async Task ShallowCloneAsync_NullRepoUrl_Throws()
    {
        var runner = new GitRunner();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            runner.ShallowCloneAsync(null!, null, "C:/tmp", null, CancellationToken.None));
    }

    [Fact]
    public async Task ShallowCloneAsync_EmptyRepoUrl_Throws()
    {
        var runner = new GitRunner();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            runner.ShallowCloneAsync("", null, "C:/tmp", null, CancellationToken.None));
    }

    [Fact]
    public async Task ShallowCloneAsync_NullTargetDirectory_Throws()
    {
        var runner = new GitRunner();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            runner.ShallowCloneAsync("https://github.com/Azure/r", null, null!, null, CancellationToken.None));
    }

    [Fact]
    public async Task ShallowCloneAsync_GitRefStartsWithDash_ThrowsInvalidOperationException()
    {
        var runner = new GitRunner();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ShallowCloneAsync(
                "https://github.com/Azure/r", "--upload-pack=evil", "C:/tmp", null, CancellationToken.None));

        Assert.Contains("Invalid git ref", ex.Message);
    }

    [Fact]
    public async Task ShallowCloneAsync_FolderPathStartsWithDash_ThrowsInvalidOperationException()
    {
        var runner = new GitRunner();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ShallowCloneAsync(
                "https://github.com/Azure/r", null, "C:/tmp", "--malicious", CancellationToken.None));

        Assert.Contains("Invalid folder path", ex.Message);
    }

    [Fact]
    public async Task ShallowCloneAsync_FolderPathWithTraversal_ThrowsInvalidOperationException()
    {
        var runner = new GitRunner();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ShallowCloneAsync(
                "https://github.com/Azure/r", null, "C:/tmp", "foo/../../etc", CancellationToken.None));

        Assert.Contains("Invalid folder path", ex.Message);
    }
}
