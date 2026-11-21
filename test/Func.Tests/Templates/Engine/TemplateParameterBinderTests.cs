// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class TemplateParameterBinderTests
{
    private const string DefaultHostData =
        """
        {
          "symbolInfo": {
            "Framework": { "longName": "framework", "shortName": "f" },
            "NoHttp": { "longName": "no-http" },
            "Auth": { "longName": "auth" },
            "TargetFramework": { "longName": "target-framework", "shortName": "tfm" },
            "Internal": {}
          }
        }
        """;

    [Theory]
    [InlineData("")]
    [InlineData("Framework=net8.0", "--framework", "net8.0")]
    [InlineData("Framework=net8.0", "-f", "net8.0")]
    [InlineData("Framework=net8.0", "--framework=net8.0")]
    [InlineData("Framework=net8.0;NoHttp=true", "myproj", "-f", "net8.0", "--no-http")]
    [InlineData("NoHttp=false", "--no-http", "false")]
    [InlineData("Auth=Anonymous", "--auth")]
    [InlineData("Auth=Function", "--auth", "Function")]
    [InlineData("TargetFramework=net8.0", "-tfm", "net8.0")]
    [InlineData("TargetFramework=net9.0;Framework=x", "--verbose", "--target-framework", "net9.0", "-f", "x")]
    public void Bind_ReturnsSuppliedValues(string expected, params string[] args)
    {
        IReadOnlyDictionary<string, string> values = Bind(DefaultHostData, args);

        values.Should().BeEquivalentTo(expected.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]));
    }

    [Theory]
    [InlineData("""{ "symbolInfo": { "Missing": { "longName": "missing" } } }""", "'Missing' is not a template parameter.")]
    [InlineData("""{ "symbolInfo": { "Framework": { "longName": "framework", "shortName": "f" }, "Auth": { "longName": "auth", "shortName": "f" } } }""", "*'--auth' conflicts with the template option '--framework'.")]
    [InlineData("""{ "symbolInfo": { "Framework": { "longName": "name" } } }""", "*'--name' conflicts with the '--name' command option.")]
    [InlineData("""{ "symbolInfo": { "NoHttp": { "longName": "verbose" } } }""", "*'--verbose' conflicts with the '--verbose' command option.")]
    [InlineData("""{ "symbolInfo": { "TargetFramework": { "longName": "target-framework", "shortName": "t" } } }""", "*'--target-framework' conflicts with the '--target-framework' command option.")]
    [InlineData("""{ "symbolInfo": { "Framework": { "longName": "-framework" } } }""", "'longName' of symbol 'Framework' must start with a letter or digit*")]
    [InlineData("""{ "symbolInfo": { "Framework": { "longName": "framework", "shortName": "f=x" } } }""", "'shortName' of symbol 'Framework' must start with a letter or digit*")]
    [InlineData("""{ "symbolInfo": { "Framework": { "longName": "target-framework" }, "TargetFramework": { "longName": "target-framework", "shortName": "tfm" } } }""", "*'--target-framework' conflicts with the template option '--target-framework'.")]
    public void Bind_InvalidHostData_Throws(string hostData, string reason)
    {
        Action act = () => Bind(hostData, []);

        act.Should().Throw<InvalidOperationException>().WithMessage($"Template 'Test.Template' has an invalid func.host.json: {reason}");
    }

    [Theory]
    [InlineData("Unrecognized option '--bogus'.", "--bogus")]
    [InlineData("Unrecognized option '--internal'.", "--internal", "x")]
    [InlineData("Unrecognized argument 'myproj'. Specify the project path before template options.", "-f", "x", "myproj")]
    [InlineData("Unrecognized argument 'template'. Specify the project path before template options.", "myproj", "template")]
    [InlineData("Unrecognized option '-f'.", "--", "-f", "x")]
    [InlineData("Option '--no-http' was specified more than once.", "--no-http", "--no-http", "false")]
    [InlineData("*--framework*", "--framework")]
    [InlineData("*-f*", "-f", "a", "-f", "b")]
    public void Bind_InvalidArguments_Throws(string message, params string[] args)
    {
        Action act = () => Bind(DefaultHostData, args);

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public void Bind_BindableOptionWithoutValue_Throws()
    {
        Action act = () => Bind(DefaultHostData, ["-tfm"], ArgumentArity.ZeroOrOne);

        act.Should().Throw<InvalidOperationException>().WithMessage("Option '-tfm' requires a value.");
    }

    [Fact]
    public void Bind_ExplicitStackOptionWithoutHostMapping_Throws()
    {
        Action act = () => Bind("{}", ["-tfm", "net11.0"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Option '-tfm' is not supported by template 'Test.Template'.");
    }

    [Fact]
    public void Bind_ImplicitStackOptionWithoutHostMapping_LeavesTemplateDefault()
    {
        Bind("{}", []).Should().BeEmpty();
    }

    [Fact]
    public void Bind_InvalidChoiceValue_ThrowsWithValidChoices()
    {
        IReadOnlyDictionary<string, ParameterChoice> choices = new Dictionary<string, ParameterChoice>
        {
            ["net8.0"] = new("net8.0", "Supported framework"),
            ["net10.0"] = new("net10.0", "Supported framework"),
        };

        Action act = () => Bind(DefaultHostData, ["--framework", "net6.0"], frameworkChoices: choices);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid value 'net6.0' for option '--framework'. Valid values: net8.0, net10.0.");
    }

    [Fact]
    public void Bind_NullArguments_Throw()
    {
        FuncTemplateInfo template = CreateTemplate(DefaultHostData, []);
        ParseResult parseResult = new Command("init").Parse([]);
        TemplateParameterBinder binder = new();

        new Action(() => binder.Bind(null!, parseResult, [], [])).Should().Throw<ArgumentNullException>();
        new Action(() => binder.Bind(template, null!, [], [])).Should().Throw<ArgumentNullException>();
        new Action(() => binder.Bind(template, parseResult, null!, [])).Should().Throw<ArgumentNullException>();
        new Action(() => binder.Bind(template, parseResult, [], null!)).Should().Throw<ArgumentNullException>();
    }

    private static IReadOnlyDictionary<string, string> Bind(
        string hostData,
        string[] args,
        ArgumentArity? targetFrameworkArity = null,
        IReadOnlyDictionary<string, ParameterChoice>? frameworkChoices = null)
    {
        FuncTemplateInfo template = CreateTemplate(hostData,
        [
            CreateParameter("Framework", "choice", choices: frameworkChoices),
            CreateParameter("NoHttp", "bool"),
            CreateParameter("Auth", "string", defaultIfOptionWithoutValue: "Anonymous"),
            CreateParameter("TargetFramework", "string"),
        ]);
        Option<string> targetFramework = new("--target-framework", "-tfm") { DefaultValueFactory = _ => "net10.0" };
        if (targetFrameworkArity is { } arity)
        {
            targetFramework.Arity = arity;
        }
        TestCommand command = new() { Options = { new Option<string>("--name", "-n"), targetFramework } };
        RootCommand root = new() { Options = { new Option<bool>("--verbose") { Recursive = true } }, Subcommands = { command } };

        ParseResult parseResult = root.Parse(["init", .. args]);

        parseResult.Errors.Should().BeEmpty();
        return new TemplateParameterBinder().Bind(template, parseResult, command.TemplateArgumentsArgument!.GetTokens(parseResult), [targetFramework]);
    }

    private static FuncTemplateInfo CreateTemplate(string hostData, IEnumerable<ITemplateParameter> parameters)
    {
        ParameterDefinitionSet parameterDefinitions = new(parameters);
        var template = Substitute.For<ITemplateInfo>();
        template.Identity.Returns("Test.Template");
        template.ParameterDefinitions.Returns(parameterDefinitions);
        return new FuncTemplateInfo(template, FuncTemplateHostData.Parse(hostData));
    }

    private static ITemplateParameter CreateParameter(
        string name,
        string dataType,
        string? defaultIfOptionWithoutValue = null,
        IReadOnlyDictionary<string, ParameterChoice>? choices = null)
    {
        var parameter = Substitute.For<ITemplateParameter>();
        parameter.Name.Returns(name);
        parameter.DataType.Returns(dataType);
        parameter.DefaultIfOptionWithoutValue.Returns(defaultIfOptionWithoutValue);
        if (choices is not null)
        {
            parameter.Choices.Returns(choices);
        }

        return parameter;
    }

    private sealed class TestCommand : FuncCliCommand
    {
        public TestCommand() : base("init", "Test command for template parameter binding.")
        {
            AddPathArgument(captureTemplateArguments: true);
        }

        protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}
