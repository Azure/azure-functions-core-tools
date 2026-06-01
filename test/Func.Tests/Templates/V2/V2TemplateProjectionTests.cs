// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.V2;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates.V2;

public class V2TemplateProjectionTests
{
    [Fact]
    public void Project_Populates_DefaultFunctionName_From_TriggerFunctionName_Input()
    {
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
                            ParamId = "trigger-functionName",
                            AssignTo = "$(FUNCTION_NAME_INPUT)",
                            DefaultValue = "httpTrigger",
                        },
                    ],
                },
            ],
        };

        FunctionTemplateInfo? info = V2TemplateProjection.Project(template, EmptyPayload(), "node");

        Assert.NotNull(info);
        Assert.Equal("httpTrigger", info!.DefaultFunctionName);
    }

    [Fact]
    public void Project_DefaultFunctionName_Picks_Input_By_AssignTo_Not_ParamId()
    {
        // ParamId is deliberately exotic — the function-name input is
        // identified by its assignTo target ($(FUNCTION_NAME_INPUT)),
        // not by a hardcoded list of paramId variants.
        NewTemplate template = new()
        {
            Id = "HttpTrigger-Python",
            Jobs =
            [
                new V2Job
                {
                    Type = "CreateNewApp",
                    Inputs =
                    [
                        new V2Input
                        {
                            ParamId = "exotic-prompt-id-the-cli-has-never-seen",
                            AssignTo = "$(FUNCTION_NAME_INPUT)",
                            DefaultValue = "http_trigger",
                        },
                    ],
                },
            ],
        };

        FunctionTemplateInfo? info = V2TemplateProjection.Project(template, EmptyPayload(), "python");

        Assert.NotNull(info);
        Assert.Equal("http_trigger", info!.DefaultFunctionName);
    }

    [Fact]
    public void Project_DefaultFunctionName_Is_Null_When_No_Function_Name_Prompt_Exists()
    {
        // No input writes to $(FUNCTION_NAME_INPUT) — the template has no
        // function-name prompt at all, just an auth-level prompt.
        NewTemplate template = new()
        {
            Id = "Bare",
            Jobs =
            [
                new V2Job
                {
                    Type = "CreateNewApp",
                    Inputs =
                    [
                        new V2Input
                        {
                            ParamId = "httpTrigger-authLevel",
                            AssignTo = "$(AUTH_LEVEL)",
                            DefaultValue = "FUNCTION",
                        },
                    ],
                },
            ],
        };

        FunctionTemplateInfo? info = V2TemplateProjection.Project(template, EmptyPayload(), "node");

        Assert.NotNull(info);
        Assert.Null(info!.DefaultFunctionName);
    }

    [Fact]
    public void Project_DefaultFunctionName_Is_Null_When_Input_Default_Missing()
    {
        NewTemplate template = new()
        {
            Id = "BareName",
            Jobs =
            [
                new V2Job
                {
                    Type = "CreateNewApp",
                    Inputs =
                    [
                        new V2Input
                        {
                            ParamId = "trigger-functionName",
                            AssignTo = "$(FUNCTION_NAME_INPUT)",
                            DefaultValue = null,
                        },
                    ],
                },
            ],
        };

        FunctionTemplateInfo? info = V2TemplateProjection.Project(template, EmptyPayload(), "node");

        Assert.NotNull(info);
        Assert.Null(info!.DefaultFunctionName);
    }

    private static V2Payload EmptyPayload() =>
        new(
            InstallDirectory: string.Empty,
            Templates: [],
            UserPrompts: new Dictionary<string, UserPromptDoc>(),
            Resources: new Dictionary<string, string>());
}
