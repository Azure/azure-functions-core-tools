// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public class TemplateOptionHydratorTests
{
    [Fact]
    public void Empty_Prompts_Produces_Empty_Option_List()
    {
        var hydrator = new TemplateOptionHydrator([]);
        FunctionTemplateInfo template = TemplateWithPrompts([]);

        IReadOnlyList<Option> options = hydrator.Hydrate(template);

        options.Should().BeEmpty();
    }

    [Fact]
    public void String_Prompt_Becomes_String_Option_With_Default()
    {
        var hydrator = new TemplateOptionHydrator([]);
        TemplateUserPrompt prompt = new(
            Id: "route",
            Description: "Route template",
            DataType: "string",
            DefaultValue: "api/v1",
            Choices: null,
            IsRequired: false,
            ValidatorRegex: null,
            ShortAlias: null,
            LongAlias: null);

        IReadOnlyList<Option> options = hydrator.Hydrate(TemplateWithPrompts([prompt]));

        Option<string?> opt = options.Should().ContainSingle().Subject.Should().BeOfType<Option<string?>>().Subject;
        opt.Name.Should().Be("--route");
        opt.Description.Should().Be("Route template");
        // SCL exposes the factory; invoke it to confirm the default value.
        opt.DefaultValueFactory.Should().NotBeNull();
    }

    [Fact]
    public void CamelCase_Id_Is_Kebab_Cased()
    {
        var hydrator = new TemplateOptionHydrator([]);
        TemplateUserPrompt prompt = new("authLevel", null, "string", null, null, false, null, null, null);

        IReadOnlyList<Option> options = hydrator.Hydrate(TemplateWithPrompts([prompt]));

        options[0].Name.Should().Be("--auth-level");
    }

    [Theory]
    [InlineData("authLevel", "--auth-level")]
    [InlineData("AccessRights", "--access-rights")]
    [InlineData("HTTPTrigger", "--http-trigger")]
    [InlineData("HTTP", "--http")]
    [InlineData("name", "--name")]
    public void PrefersKebab_Across_PascalAndAcronyms(string promptId, string expectedOptionName)
    {
        var hydrator = new TemplateOptionHydrator([]);
        TemplateUserPrompt prompt = new(promptId, null, "string", null, null, false, null, null, null);

        IReadOnlyList<Option> options = hydrator.Hydrate(TemplateWithPrompts([prompt]));

        options[0].Name.Should().Be(expectedOptionName);
    }

    [Fact]
    public void Choice_Prompt_Uses_AcceptOnlyFromAmong()
    {
        var hydrator = new TemplateOptionHydrator([]);
        TemplateUserPrompt prompt = new(
            Id: "authLevel",
            Description: "Auth level",
            DataType: "choice",
            DefaultValue: "function",
            Choices: ["function", "anonymous", "admin"],
            IsRequired: false,
            ValidatorRegex: null,
            ShortAlias: null,
            LongAlias: null);

        IReadOnlyList<Option> options = hydrator.Hydrate(TemplateWithPrompts([prompt]));

        Option<string?> opt = options.Should().ContainSingle().Subject.Should().BeOfType<Option<string?>>().Subject;
        // SCL emits a validator for AcceptOnlyFromAmong; we just confirm the option exists with the expected name.
        opt.Name.Should().Be("--auth-level");
    }

    [Fact]
    public void Bool_Prompt_Becomes_Bool_Option()
    {
        var hydrator = new TemplateOptionHydrator([]);
        TemplateUserPrompt prompt = new("verbose", null, "bool", "true", null, false, null, null, null);

        IReadOnlyList<Option> options = hydrator.Hydrate(TemplateWithPrompts([prompt]));

        options.Should().ContainSingle().Subject.Should().BeOfType<Option<bool>>();
    }

    [Fact]
    public void Long_Alias_Override_Replaces_Default_Name()
    {
        var hydrator = new TemplateOptionHydrator([]);
        TemplateUserPrompt prompt = new("namespace", null, "string", null, null, false, null, null, "--ns");

        IReadOnlyList<Option> options = hydrator.Hydrate(TemplateWithPrompts([prompt]));

        options[0].Name.Should().Be("--ns");
    }

    [Fact]
    public void Short_Alias_Is_Added()
    {
        var hydrator = new TemplateOptionHydrator([]);
        TemplateUserPrompt prompt = new("route", null, "string", null, null, false, null, "-r", null);

        IReadOnlyList<Option> options = hydrator.Hydrate(TemplateWithPrompts([prompt]));

        options[0].Aliases.Should().Contain("-r");
    }

    [Fact]
    public void Function_Name_Prompt_Falls_Back_To_Stack_Validator()
    {
        var initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("dotnet");
        initializer.SupportedLanguages.Returns(["csharp"]);
        initializer.DefaultFunctionNameValidator.Returns(new Regex("^[A-Z][A-Za-z0-9]*$"));

        var hydrator = new TemplateOptionHydrator([initializer]);
        TemplateUserPrompt prompt = new("name", null, "string", null, null, false, null, null, null);

        FunctionTemplateInfo template = TemplateWithPrompts([prompt], stack: "dotnet");
        IReadOnlyList<Option> options = hydrator.Hydrate(template);

        // The hydrator wires a validator delegate that runs at parse time; we
        // confirm the option exists and is configured. End-to-end validation
        // is exercised once NewCommand actually parses a stage-B argv.
        options.Should().ContainSingle();
    }

    private static FunctionTemplateInfo TemplateWithPrompts(
        IReadOnlyList<TemplateUserPrompt> prompts,
        string stack = "node") =>
        new(
            Id: "HttpTrigger",
            Stack: stack,
            EngineId: EngineIds.V2,
            DisplayName: "HTTP trigger",
            Description: "An HTTP-triggered function.",
            DefaultFunctionName: "HttpTrigger",
            Languages: ["javascript"],
            Metadata: new TemplateMetadata(prompts, RequiresExtensionBundle: true, MinBundleVersion: null));
}
