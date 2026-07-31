// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Unit tests for the func-owned add-reference post action: a targeted,
/// idempotent <c>.csproj</c> edit. Uses an in-memory filesystem.
/// </summary>
public class AddReferencePostActionHandlerTests
{
    private const string Reference = "Microsoft.Azure.Functions.Worker.Extensions.Http";
    private static readonly string _projectDir = Path.Combine(Path.GetTempPath(), "func-addref-project");
    private static readonly string _projectFile = Path.Combine(_projectDir, "MyApp.csproj");

    private const string BaseCsproj =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
        "  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n" +
        "  <ItemGroup>\n    <PackageReference Include=\"Microsoft.Azure.Functions.Worker\" Version=\"1.0.0\" />\n  </ItemGroup>\n" +
        "</Project>\n";

    [Fact]
    public async Task ExecuteAsync_AddsPackageReferenceOnce()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        fs.Seed(_projectFile, BaseCsproj);
        var handler = new AddReferencePostActionHandler(fs);

        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, version: "3.0.0"), CancellationToken.None);

        FuncPostActionResult.Succeeded succeeded = result.Should().BeOfType<FuncPostActionResult.Succeeded>().Subject;
        succeeded.ModifiedFiles.Should().ContainSingle().Which.Should().Be("MyApp.csproj");
        string written = fs.Peek(_projectFile)!;
        written.Should().Contain($"Include=\"{Reference}\"").And.Contain("Version=\"3.0.0\"");
    }

    [Fact]
    public async Task ExecuteAsync_RunTwice_IsIdempotent()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        fs.Seed(_projectFile, BaseCsproj);
        var handler = new AddReferencePostActionHandler(fs);

        await handler.ExecuteAsync(Context(fs, version: "3.0.0"), CancellationToken.None);
        FuncPostActionResult second = await handler.ExecuteAsync(Context(fs, version: "3.0.0"), CancellationToken.None);

        FuncPostActionResult.Succeeded succeeded = second.Should().BeOfType<FuncPostActionResult.Succeeded>().Subject;
        succeeded.ModifiedFiles.Should().BeEmpty("a reference that is already present is a no-op");

        int occurrences = CountOccurrences(fs.Peek(_projectFile)!, $"Include=\"{Reference}\"");
        occurrences.Should().Be(1, "the reference must not be duplicated on re-run");
    }

    [Fact]
    public async Task ExecuteAsync_MissingProject_ContinueOnErrorTrue_FailsNonFatally()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        var handler = new AddReferencePostActionHandler(fs);

        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, version: "3.0.0", continueOnError: true), CancellationToken.None);

        FuncPostActionResult.Failed failed = result.Should().BeOfType<FuncPostActionResult.Failed>().Subject;
        failed.ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_MissingProject_ContinueOnErrorFalse_FailsFatally()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        var handler = new AddReferencePostActionHandler(fs);

        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, version: "3.0.0", continueOnError: false), CancellationToken.None);

        FuncPostActionResult.Failed failed = result.Should().BeOfType<FuncPostActionResult.Failed>().Subject;
        failed.ContinueOnError.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_NullContext_Throws()
    {
        var handler = new AddReferencePostActionHandler(new InMemoryFuncTemplateFileSystem());

        await FluentActions.Awaiting(() => handler.ExecuteAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static FuncPostActionContext Context(InMemoryFuncTemplateFileSystem fs, string version, bool continueOnError = false)
    {
        _ = fs;
        IPostAction action = Substitute.For<IPostAction>();
        action.ActionId.Returns(FuncPostActionIds.AddReference);
        action.ContinueOnError.Returns(continueOnError);
        action.ManualInstructions.Returns("Add the package reference manually.");
        action.Args.Returns(new Dictionary<string, string>
        {
            ["referenceType"] = "package",
            ["reference"] = Reference,
            ["version"] = version,
            ["projectFileExtensions"] = ".csproj",
        });

        return new FuncPostActionContext(
            action,
            _projectDir,
            [],
            _projectDir,
            "HttpTriggerFunc",
            new Dictionary<string, string?>());
    }
}
