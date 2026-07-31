// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Unit tests for the func-owned append post action (Python v2 model),
/// covering the full flow matrix, the duplicate guard, and staging isolation
/// (a failed append writes nothing to the project). Uses an in-memory
/// filesystem so no real disk is touched.
/// </summary>
public class AppendPostActionHandlerTests
{
    private const string FunctionName = "HttpTriggerFunc";
    private const string DefaultTarget = "function_app.py";

    private static readonly string _projectDir = Path.Combine(Path.GetTempPath(), "func-append-project");
    private static readonly string _stagingDir = Path.Combine(Path.GetTempPath(), "func-append-staging");
    private static readonly string _snippet =
        "@app.route(route=\"http_trigger\")\ndef HttpTriggerFunc(req: func.HttpRequest) -> func.HttpResponse:\n    return func.HttpResponse(\"ok\")";

    [Fact]
    public async Task ExecuteAsync_NoFileArg_AppExists_AppendsToApp()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        fs.Seed(Path.Combine(_stagingDir, "snippet.py"), _snippet);
        fs.Seed(Path.Combine(_projectDir, DefaultTarget), "import azure.functions as func\n\napp = func.FunctionApp()\n");
        var handler = new AppendPostActionHandler(fs);

        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, DefaultTarget, "app"), CancellationToken.None);

        FuncPostActionResult.Succeeded succeeded = result.Should().BeOfType<FuncPostActionResult.Succeeded>().Subject;
        succeeded.ModifiedFiles.Should().ContainSingle().Which.Should().Be(DefaultTarget);
        string written = fs.Peek(Path.Combine(_projectDir, DefaultTarget))!;
        written.Should().Contain("app = func.FunctionApp()").And.Contain($"def {FunctionName}(");
    }

    [Fact]
    public async Task ExecuteAsync_AppendToCrlfFile_PreservesExistingLineEndingsAndBytes()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        fs.Seed(Path.Combine(_stagingDir, "snippet.py"), _snippet);
        string crlfBody = "import azure.functions as func\r\n\r\napp = func.FunctionApp()\r\n";
        fs.Seed(Path.Combine(_projectDir, DefaultTarget), crlfBody);
        var handler = new AppendPostActionHandler(fs);

        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, DefaultTarget, "app"), CancellationToken.None);

        result.Should().BeOfType<FuncPostActionResult.Succeeded>();
        string written = fs.Peek(Path.Combine(_projectDir, DefaultTarget))!;
        // The user's existing bytes are left exactly as they were...
        written.Should().StartWith(crlfBody);
        // ...and the appended block matches the file's CRLF style: no lone LF remains.
        written.Replace("\r\n", string.Empty).Should().NotContain("\n");
        written.Should().Contain($"def {FunctionName}(");
    }

    [Fact]
    public async Task ExecuteAsync_NoFileArg_AppMissing_CreatesAppHeaderThenAppends()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        fs.Seed(Path.Combine(_stagingDir, "snippet.py"), _snippet);
        var handler = new AppendPostActionHandler(fs);

        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, DefaultTarget, "app"), CancellationToken.None);

        result.Should().BeOfType<FuncPostActionResult.Succeeded>();
        string written = fs.Peek(Path.Combine(_projectDir, DefaultTarget))!;
        written.Should().StartWith("import azure.functions as func");
        written.Should().Contain("app = func.FunctionApp(http_auth_level=func.AuthLevel.FUNCTION)");
        written.Should().Contain($"def {FunctionName}(");
    }

    [Fact]
    public async Task ExecuteAsync_FileArg_BlueprintMissing_CreatesBlueprintAndPrintsRegistration()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        fs.Seed(Path.Combine(_stagingDir, "snippet.py"), _snippet);
        var handler = new AppendPostActionHandler(fs);

        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, "api.py", "bp"), CancellationToken.None);

        FuncPostActionResult.Succeeded succeeded = result.Should().BeOfType<FuncPostActionResult.Succeeded>().Subject;
        succeeded.Instructions.Should().Contain(i => i.Contains("from api import bp"));
        succeeded.Instructions.Should().Contain(i => i.Contains("app.register_functions(bp)"));

        // Blueprint creation must not auto-edit function_app.py.
        fs.FileExists(Path.Combine(_projectDir, DefaultTarget)).Should().BeFalse();
        string written = fs.Peek(Path.Combine(_projectDir, "api.py"))!;
        written.Should().Contain("bp = func.Blueprint()").And.Contain($"def {FunctionName}(");
    }

    [Fact]
    public async Task ExecuteAsync_FileArg_BlueprintExists_AppendsToBlueprint()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        fs.Seed(Path.Combine(_stagingDir, "snippet.py"), _snippet);
        fs.Seed(Path.Combine(_projectDir, "api.py"), "import azure.functions as func\n\nbp = func.Blueprint()\n");
        var handler = new AppendPostActionHandler(fs);

        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, "api.py", "bp"), CancellationToken.None);

        result.Should().BeOfType<FuncPostActionResult.Succeeded>();
        string written = fs.Peek(Path.Combine(_projectDir, "api.py"))!;
        written.Should().Contain("bp = func.Blueprint()").And.Contain($"def {FunctionName}(");
        // Existing blueprint file must be preserved, not replaced with a fresh header.
        written.IndexOf("bp = func.Blueprint()", StringComparison.Ordinal)
            .Should().Be(written.LastIndexOf("bp = func.Blueprint()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateFunction_FailsAndDoesNotWrite()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        fs.Seed(Path.Combine(_stagingDir, "snippet.py"), _snippet);
        fs.Seed(
            Path.Combine(_projectDir, DefaultTarget),
            $"import azure.functions as func\n\napp = func.FunctionApp()\n\n@app.route()\ndef {FunctionName}(req):\n    pass\n");
        var handler = new AppendPostActionHandler(fs);

        // --force cannot override the duplicate guard: even a continue-on-error action stays fatal.
        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, DefaultTarget, "app", continueOnError: true), CancellationToken.None);

        FuncPostActionResult.Failed failed = result.Should().BeOfType<FuncPostActionResult.Failed>().Subject;
        failed.ContinueOnError.Should().BeFalse();
        failed.Message.Should().Contain(FunctionName);
        fs.WrittenPaths.Should().BeEmpty("the duplicate guard must not touch the project");
    }

    [Fact]
    public async Task ExecuteAsync_WriteFailure_PointsAtStagedSnippetForRecovery()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        string stagedPath = Path.Combine(_stagingDir, "snippet.py");
        fs.Seed(stagedPath, _snippet);
        fs.ThrowOnWrite = true;
        var handler = new AppendPostActionHandler(fs);

        FuncPostActionResult result = await handler.ExecuteAsync(Context(fs, DefaultTarget, "app"), CancellationToken.None);

        FuncPostActionResult.Failed failed = result.Should().BeOfType<FuncPostActionResult.Failed>().Subject;
        failed.Message.Should().Contain(Path.GetFullPath(stagedPath));
        failed.Exception.Should().BeOfType<IOException>();
    }

    [Fact]
    public async Task ExecuteAsync_DeleteStagedFileTrue_RemovesStagedSnippetAfterSuccess()
    {
        var fs = new InMemoryFuncTemplateFileSystem();
        string stagedPath = Path.Combine(_stagingDir, "snippet.py");
        fs.Seed(stagedPath, _snippet);
        var handler = new AppendPostActionHandler(fs);

        await handler.ExecuteAsync(Context(fs, DefaultTarget, "app"), CancellationToken.None);

        fs.FileExists(stagedPath).Should().BeFalse("deleteStagedFile=true removes the staged snippet");
    }

    [Fact]
    public async Task ExecuteAsync_NullContext_Throws()
    {
        var handler = new AppendPostActionHandler(new InMemoryFuncTemplateFileSystem());

        await FluentActions.Awaiting(() => handler.ExecuteAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    private static FuncPostActionContext Context(
        InMemoryFuncTemplateFileSystem fs,
        string targetFile,
        string appObject,
        bool continueOnError = false)
    {
        _ = fs;
        IPostAction action = Substitute.For<IPostAction>();
        action.ActionId.Returns(FuncPostActionIds.Append);
        action.ContinueOnError.Returns(continueOnError);
        action.ManualInstructions.Returns("Append the snippet manually.");
        action.Args.Returns(new Dictionary<string, string>
        {
            ["targetFileParam"] = "AppFile",
            ["appObjectParam"] = "AppObject",
            ["deleteStagedFile"] = "true",
        });

        var parameterValues = new Dictionary<string, string?>
        {
            ["AppFile"] = targetFile,
            ["AppObject"] = appObject,
        };

        return new FuncPostActionContext(
            action,
            _stagingDir,
            ["snippet.py"],
            _projectDir,
            FunctionName,
            parameterValues);
    }
}
