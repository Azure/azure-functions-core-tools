// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.PostActions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.PostActions;

public sealed class PostActionContextExtensionsTests
{
    private readonly IPostActionFileMatcher _matcher = Substitute.For<IPostActionFileMatcher>();
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), "func-post-action-base");

    [Fact]
    public void GetConfiguredFiles_CustomArgumentAndFilter_ResolvesTargetsInOrder()
    {
        _matcher.MatchTargetPaths("*.csproj").Returns(["one.csproj", "ignore.txt"]);
        _matcher.MatchTargetPaths("*.fsproj").Returns(["nested/two.fsproj", "one.csproj"]);
        PostActionContext context = Context(new Dictionary<string, string>
        {
            ["files"] = "unused",
            ["projects"] = "*.csproj;*.fsproj",
        });

        IReadOnlyList<string> result = context.GetConfiguredFiles("projects", path => !path.EndsWith(".txt", StringComparison.Ordinal));

        result.Should().Equal(
            Path.Combine(_outputDirectory, "one.csproj"),
            Path.Combine(_outputDirectory, "nested", "two.fsproj"),
            Path.Combine(_outputDirectory, "one.csproj"));
        Received.InOrder(() =>
        {
            _matcher.MatchTargetPaths("*.csproj");
            _matcher.MatchTargetPaths("*.fsproj");
        });
    }

    [Fact]
    public void GetConfiguredFiles_ArgumentName_IsRequired()
    {
        typeof(PostActionContextExtensions).GetMethod(nameof(PostActionContextExtensions.GetConfiguredFiles))!
            .GetParameters().Single(parameter => parameter.Name == "argumentName").IsOptional.Should().BeFalse();
    }

    [Fact]
    public void FileHelpers_NullContextOrInvalidArguments_Throw()
    {
        PostActionContext context = Context(new Dictionary<string, string>());

        FluentActions.Invoking(() => PostActionContextExtensions.GetConfiguredFiles(null!, "files")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => context.GetConfiguredFiles(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => context.GetConfiguredFiles(" ")).Should().Throw<ArgumentException>();
    }

    private PostActionContext Context(IReadOnlyDictionary<string, string> arguments)
        => new(Guid.Empty, arguments, null, null, false, _outputDirectory, [], [], fileMatcher: _matcher);
}
