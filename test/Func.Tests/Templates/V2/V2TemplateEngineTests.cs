// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.V2;

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

        var created = result.Should().BeOfType<TemplateApplicationResult.Created>().Subject;
        string expectedPath = Path.GetFullPath(Path.Combine(_workingDir, "src", "functions", "MyHttp.js"));
        created.Files.Should().ContainSingle();
        created.Files[0].Should().Be(expectedPath);
        File.Exists(expectedPath).Should().BeTrue();
        string content = File.ReadAllText(expectedPath);
        content.Should().Contain("hello MyHttp");
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

        result.Should().BeOfType<TemplateApplicationResult.AlreadyExists>()
            .Which.ExistingFiles.Should().ContainSingle();
        File.ReadAllText(target).Should().Be("preexisting");
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

        result.Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllText(target).Should().Be("new content");
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

        result.Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllText(Path.Combine(_workingDir, "config.txt")).Should().Be("auth=anonymous");
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

        var failed = result.Should().BeOfType<TemplateApplicationResult.Failed>().Subject;
        failed.Failure.Should().BeOfType<TemplateApplicationFailure.InvalidPrompt>()
            .Which.PromptId.Should().Be("queueName");
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

        result.Should().BeOfType<TemplateApplicationResult.Failed>()
            .Which.Failure.Should().BeOfType<TemplateApplicationFailure.ProviderError>();
    }

    [Theory]
    [InlineData("HttpTrigger-Python", "HttpTrigger_Python")]
    [InlineData("HttpTrigger", "HttpTrigger")]
    [InlineData("My-Http-Trigger", "My_Http_Trigger")]
    [InlineData("My.Func Name", "My_Func_Name")]
    [InlineData("1HttpTrigger", "_1HttpTrigger")]
    [InlineData("--weird/name", "__weird_name")]
    [InlineData("already_valid", "already_valid")]
    public void SanitizeFunctionIdentifier_ProducesValidIdentifier(string input, string expected)
    {
        V2TemplateEngine.SanitizeFunctionIdentifier(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("HttpTrigger-Python", "HttpTrigger_Python")]
    [InlineData("1HttpTrigger", "_1HttpTrigger")]
    [InlineData("My.Func Name", "My_Func_Name")]
    public void Apply_FunctionName_With_Invalid_Identifier_Chars_Is_Sanitized_In_Generated_Code(string functionName, string sanitized)
    {
        // Models the Python v2 + Node v4 templates: $(FUNCTION_NAME_INPUT) is
        // rendered as both a code identifier (`def X(...)` / `function X(...)`)
        // and as the file name. Without sanitization, a name like
        // "HttpTrigger-Python" produces "def HttpTrigger-Python(...)" and fails
        // at parse time with SyntaxError.
        NewTemplate template = new()
        {
            Id = "HttpTrigger-Python",
            Name = "HTTP trigger",
            Files = new Dictionary<string, string>
            {
                ["function_app.py"] =
                    "import azure.functions as func\n\n" +
                    "app = func.FunctionApp()\n\n" +
                    "@app.route(route=\"$(FUNCTION_NAME_INPUT)\")\n" +
                    "def $(FUNCTION_NAME_INPUT)(req: func.HttpRequest) -> func.HttpResponse:\n" +
                    "    return func.HttpResponse('ok')\n",
            },
            Actions =
            [
                new V2Action
                {
                    Type = "GetTemplateFileContent",
                    FilePath = "function_app.py",
                    AssignTo = "$(BODY)",
                },
                new V2Action
                {
                    Type = "WriteToFile",
                    FilePath = "src/functions/$(FUNCTION_NAME_INPUT).py",
                    FileContent = "$(BODY)",
                },
            ],
        };

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName,
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        string expectedPath = Path.GetFullPath(Path.Combine(_workingDir, "src", "functions", $"{sanitized}.py"));
        result.Should().BeOfType<TemplateApplicationResult.Created>().Which.Files.Single().Should().Be(expectedPath);

        string content = File.ReadAllText(expectedPath);
        content.Should().Contain($"def {sanitized}(");
        content.Should().NotContain($"def {functionName}(");
    }

    [Fact]
    public void Apply_FunctionName_Supplied_Via_Prompt_Value_Is_Also_Sanitized()
    {
        // V2EngineProvider re-supplies the function name as the prompt value
        // (for the well-known function-name prompt ids). The engine must
        // sanitize that path too, not just the seeded `functionName`
        // parameter, otherwise the prompt write clobbers the seed.
        NewTemplate template = new()
        {
            Id = "HttpTrigger-JavaScript",
            Name = "HTTP trigger",
            Jobs =
            [
                new V2Job
                {
                    Type = "CreateNewApp",
                    Inputs =
                    [
                        new V2Input
                        {
                            AssignTo = "$(FUNCTION_NAME_INPUT)",
                            ParamId = "trigger-functionName",
                            Required = true,
                        },
                    ],
                    Actions = ["readFn", "writeFn"],
                },
            ],
            Files = new Dictionary<string, string>
            {
                ["function.ts"] =
                    "export async function $(FUNCTION_NAME_INPUT)(req, ctx) { return {}; }\n" +
                    "app.http('$(FUNCTION_NAME_INPUT)', { handler: $(FUNCTION_NAME_INPUT) });\n",
            },
            Actions =
            [
                new V2Action
                {
                    Name = "readFn",
                    Type = "GetTemplateFileContent",
                    FilePath = "function.ts",
                    AssignTo = "$(FN_CONTENT)",
                },
                new V2Action
                {
                    Name = "writeFn",
                    Type = "WriteToFile",
                    FilePath = "src/functions/$(FUNCTION_NAME_INPUT).ts",
                    Source = "$(FN_CONTENT)",
                },
            ],
        };

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "HttpTrigger-Python",
            optionValuesByPromptId: new Dictionary<string, string?>
            {
                ["trigger-functionName"] = "HttpTrigger-Python",
            },
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        string expectedPath = Path.GetFullPath(Path.Combine(_workingDir, "src", "functions", "HttpTrigger_Python.ts"));
        result.Should().BeOfType<TemplateApplicationResult.Created>().Which.Files.Single().Should().Be(expectedPath);

        string content = File.ReadAllText(expectedPath);
        content.Should().Contain("export async function HttpTrigger_Python(");
        content.Should().Contain("handler: HttpTrigger_Python");
        content.Should().NotContain("HttpTrigger-Python");
    }

    [Fact]
    public void Apply_MultiJob_PicksCreateNewApp_When_Target_Missing()
    {
        NewTemplate template = MakeCreateOrAppendTemplate();

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "MyFn",
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        string target = Path.GetFullPath(Path.Combine(_workingDir, "function_app.py"));
        result.Should().BeOfType<TemplateApplicationResult.Created>().Which.Files.Single().Should().Be(target);

        string content = File.ReadAllText(target);
        content.Should().StartWith("# fresh app skeleton");
        content.Should().NotContain("@app.route");
    }

    [Fact]
    public void Apply_MultiJob_PicksAppendToFile_When_Target_Exists()
    {
        string target = Path.Combine(_workingDir, "function_app.py");
        File.WriteAllText(target, "# preexisting\n");

        NewTemplate template = MakeCreateOrAppendTemplate();

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "MyFn",
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        result.Should().BeOfType<TemplateApplicationResult.Created>()
            .Which.Files.Single().Should().Be(Path.GetFullPath(target));

        string content = File.ReadAllText(target);
        content.Should().StartWith("# preexisting");
        content.Should().Contain("@app.route(MyFn)");
    }

    [Fact]
    public void Apply_MultiJob_AppendToFile_Inserts_Newline_When_Tail_Missing()
    {
        // Models the state left behind by a prior AppendToFile whose snippet
        // had no trailing newline (the real python function_body.py shape).
        string target = Path.Combine(_workingDir, "function_app.py");
        File.WriteAllText(target, "prior_no_newline");

        NewTemplate template = MakeCreateOrAppendTemplate();

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "Next",
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        result.Should().BeOfType<TemplateApplicationResult.Created>();
        string content = File.ReadAllText(target);
        content.Should().NotContain("prior_no_newline@app.route");
        content.Should().MatchRegex(@"prior_no_newline\r?\n@app\.route\(Next\)");
    }

    [Fact]
    public void Apply_MultiJob_With_No_CreateNewApp_Falls_Back_To_First_Job()
    {
        NewTemplate template = new()
        {
            Id = "BlueprintOnly",
            Jobs =
            [
                new V2Job
                {
                    Type = "CreateNewBlueprint",
                    Actions = ["write_blueprint"],
                },
                new V2Job
                {
                    Type = "AppendToBlueprint",
                    Actions = ["append_blueprint"],
                },
            ],
            Actions =
            [
                new V2Action
                {
                    Name = "write_blueprint",
                    Type = "WriteToFile",
                    FilePath = "blueprint.py",
                    FileContent = "blueprint body",
                },
                new V2Action
                {
                    Name = "append_blueprint",
                    Type = "AppendToFile",
                    FilePath = "blueprint.py",
                    FileContent = "extra",
                },
            ],
        };

        var engine = new V2TemplateEngine();
        TemplateApplicationResult result = engine.Apply(
            template,
            functionName: "MyFn",
            optionValuesByPromptId: new Dictionary<string, string?>(),
            workingDirectory: new DirectoryInfo(_workingDir),
            force: false);

        result.Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllText(Path.Combine(_workingDir, "blueprint.py")).Should().Be("blueprint body");
    }

    private static NewTemplate MakeCreateOrAppendTemplate() => new()
    {
        Id = "HttpTrigger-Python",
        ProgrammingModel = "v2",
        Language = "python",
        Jobs =
        [
            new V2Job
            {
                Type = "CreateNewApp",
                Inputs =
                [
                    new V2Input
                    {
                        ParamId = "app-fileName",
                        AssignTo = "$(APP_FILENAME)",
                        DefaultValue = "function_app.py",
                        Required = true,
                    },
                ],
                Actions = ["read_app", "write_app"],
            },
            new V2Job
            {
                Type = "AppendToFile",
                Inputs =
                [
                    new V2Input
                    {
                        ParamId = "app-selectedFileName",
                        AssignTo = "$(SELECTED_FILEPATH)",
                        DefaultValue = "function_app.py",
                        Required = true,
                    },
                ],
                Actions = ["read_body", "append_body"],
            },
        ],
        Actions =
        [
            new V2Action
            {
                Name = "read_app",
                Type = "GetTemplateFileContent",
                FilePath = "function_app.py",
                AssignTo = "$(APP_CONTENT)",
            },
            new V2Action
            {
                Name = "write_app",
                Type = "WriteToFile",
                FilePath = "$(APP_FILENAME)",
                Source = "$(APP_CONTENT)",
            },
            new V2Action
            {
                Name = "read_body",
                Type = "GetTemplateFileContent",
                FilePath = "function_body.py",
                AssignTo = "$(BODY_CONTENT)",
            },
            new V2Action
            {
                Name = "append_body",
                Type = "AppendToFile",
                FilePath = "$(SELECTED_FILEPATH)",
                Source = "$(BODY_CONTENT)",
            },
        ],
        Files = new Dictionary<string, string>
        {
            ["function_app.py"] = "# fresh app skeleton",
            ["function_body.py"] = "@app.route($(FUNCTION_NAME_INPUT))",
        },
    };
}
