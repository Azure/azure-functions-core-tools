// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Projects;

public sealed class ProjectTemplateInputLeaseTests
{
    [Fact]
    public void Pin_HoldsEveryInputUntilTheLeaseIsDisposed()
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        MemoryStream first = new();
        MemoryStream second = new();
        string firstPath = Path.GetFullPath("first.csproj");
        string secondPath = Path.GetFullPath("Directory.Build.props");
        fileSystem.OpenReadNoFollow(firstPath, CancellationToken.None).Returns(first);
        fileSystem.OpenReadNoFollow(secondPath, CancellationToken.None).Returns(second);
        ProjectTemplateInputLease factory = new(fileSystem);

        IDisposable lease = factory.Pin([firstPath, secondPath], CancellationToken.None);

        Assert.True(first.CanRead);
        Assert.True(second.CanRead);
        lease.Dispose();
        Assert.False(first.CanRead);
        Assert.False(second.CanRead);
    }

    [Fact]
    public void Pin_RejectsAliasedInputPathsBeforeOpeningThem()
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        ProjectTemplateInputLease factory = new(fileSystem);
        string path = Path.GetFullPath("App.csproj");

        Assert.Throws<InvalidDataException>(() =>
            factory.Pin([path, path.ToUpperInvariant()], CancellationToken.None));

        fileSystem.DidNotReceive().OpenReadNoFollow(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Pin_FailureClosesPreviouslyOpenedInputsAndPreservesTheCause()
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        MemoryStream first = new();
        string firstPath = Path.GetFullPath("first.csproj");
        string missingPath = Path.GetFullPath("missing.props");
        fileSystem.OpenReadNoFollow(firstPath, CancellationToken.None).Returns(first);
        fileSystem.OpenReadNoFollow(missingPath, CancellationToken.None).Returns(_ => throw new IOException("missing"));
        ProjectTemplateInputLease factory = new(fileSystem);

        IOException exception = Assert.Throws<IOException>(() =>
            factory.Pin([firstPath, missingPath], CancellationToken.None));

        Assert.Equal("missing", exception.Message);
        Assert.False(first.CanRead);
    }
}
