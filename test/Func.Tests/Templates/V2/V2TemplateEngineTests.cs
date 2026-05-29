// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.V2;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates.V2;

public class V2TemplateEngineTests : IDisposable
{
    private readonly string _workingDir;

    public V2TemplateEngineTests()
    {
        _workingDir = Path.Combine(Path.GetTempPath(), "func-new-pr2-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workingDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Apply_WriteToFile_Substitutes_FunctionName_And_Writes()
    {
        NewTemplate template = new()
        {
            Id = "HttpTrigger-JavaScript",
            Name = "HTTP trigger",
            Files = new Dictionary<string, string>
            {
                ["index.js"] = "module.exports = async function (context, req) { return { body: 'hello $(FUNCTION_NAME_INPUT)' }; };",
            },
            Actions =
            [
                new V2Action
                {
                    Type = "GetTemplateFileContent",
                    FilePath = "index.js",
                    AssignTo = "$(INDEX_CONTENT)",
                },
                new V2Action
                {
                    Type = "WriteToFile",
                    FilePath = "src/functions/$(FUNCTION_NAME_INPUT).js",
                    FileContent = "$(INDEX_CONTENT)",
                },
            ],
        };

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "MyHttp",
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        var created = Assert.IsType<TemplateApplicationResult.Created>(result);
        string expectedPath = Path.GetFullPath(Path.Combine(_workingDir, "src", "functions", "MyHttp.js"));
        Assert.Single(created.Files);
        Assert.Equal(expectedPath, created.Files[0]);
        Assert.True(File.Exists(expectedPath));
        string content = File.ReadAllText(expectedPath);
        Assert.Contains("hello MyHttp", content);
    }

    [Fact]
    public void Apply_Existing_File_Without_Force_Returns_AlreadyExists()
    {
        string target = Path.Combine(_workingDir, "out.txt");
        File.WriteAllText(target, "preexisting");

        NewTemplate template = new()
        {
            Id = "DummyTemplate",
            Actions =
            [
                new V2Action
                {
                    Type = "WriteToFile",
                    FilePath = "out.txt",
                    FileContent = "new content",
                },
            ],
        };

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "Fn",
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        var existed = Assert.IsType<TemplateApplicationResult.AlreadyExists>(result);
        Assert.Single(existed.ExistingFiles);
        Assert.Equal("preexisting", File.ReadAllText(target));
    }

    [Fact]
    public void Apply_Force_Overwrites_Existing_File()
    {
        string target = Path.Combine(_workingDir, "out.txt");
        File.WriteAllText(target, "preexisting");

        NewTemplate template = new()
        {
            Id = "DummyTemplate",
            Actions =
            [
                new V2Action
                {
                    Type = "WriteToFile",
                    FilePath = "out.txt",
                    FileContent = "new content",
                },
            ],
        };

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "Fn",
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: true);

        Assert.IsType<TemplateApplicationResult.Created>(result);
        Assert.Equal("new content", File.ReadAllText(target));
    }

    [Fact]
    public void Apply_Substitutes_Option_Values_From_PromptId_Map()
    {
        NewTemplate template = new()
        {
            Id = "HttpTrigger",
            Jobs =
            [
                new V2Job
                {
                    Type = "CreateNewApp",
                    Inputs =
                    [
                        new V2Input
                        {
                            ParamId = "authLevel",
                            AssignTo = "$(AUTH_LEVEL)",
                            DefaultValue = "function",
                        },
                    ],
                },
            ],
            Actions =
            [
                new V2Action
                {
                    Type = "WriteToFile",
                    FilePath = "config.txt",
                    FileContent = "auth=$(AUTH_LEVEL)",
                },
            ],
        };

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "Fn",
            optionValuesByPromptId: new Dictionary<string, string?>
            {
                ["authLevel"] = "anonymous",
            },
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        Assert.IsType<TemplateApplicationResult.Created>(result);
        Assert.Equal("auth=anonymous", File.ReadAllText(Path.Combine(_workingDir, "config.txt")));
    }

    [Fact]
    public void Apply_Required_Input_Without_Value_Fails_InvalidPrompt()
    {
        NewTemplate template = new()
        {
            Id = "HttpTrigger",
            Jobs =
            [
                new V2Job
                {
                    Type = "CreateNewApp",
                    Inputs =
                    [
                        new V2Input
                        {
                            ParamId = "queueName",
                            AssignTo = "$(QUEUE_NAME)",
                            Required = true,
                        },
                    ],
                },
            ],
        };

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "Fn",
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        var failed = Assert.IsType<TemplateApplicationResult.Failed>(result);
        var invalid = Assert.IsType<TemplateApplicationFailure.InvalidPrompt>(failed.Failure);
        Assert.Equal("queueName", invalid.PromptId);
    }

    [Fact]
    public void Apply_Unsupported_Action_Type_Surfaces_ProviderError()
    {
        NewTemplate template = new()
        {
            Id = "T",
            Actions =
            [
                new V2Action { Type = "RunSomeShellCommand" },
            ],
        };

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "Fn",
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        var failed = Assert.IsType<TemplateApplicationResult.Failed>(result);
        Assert.IsType<TemplateApplicationFailure.ProviderError>(failed.Failure);
    }
}
