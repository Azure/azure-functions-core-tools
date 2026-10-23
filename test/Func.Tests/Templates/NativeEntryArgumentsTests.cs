// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Globalization;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeEntryArgumentsTests
{
    [Theory]
    [InlineData("--verbose", "new", "--greeting", "new", "project", "-t", "http")]
    [InlineData("--verbose=true", "new", "--greeting=new", "project", "-t", "http")]
    [InlineData("new", "--greeting", "project", "project", "-t", "http", "--verbose")]
    [InlineData("new", "project", "-t", "new", "--greeting=new")]
    public void Read_FullGraphIdentifierAndUnknownScalarTail_PreservesExactRawInput(params string[] tokens)
    {
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        command.PathArgument!.CustomParser = _ => WorkingDirectory.FromCwd();
        FuncRootCommand root = [command];
        var parsed = root.Parse(tokens, new() { EnablePosixBundling = false, ResponseFileTokenReplacer = null });
        parsed.Errors.Should().BeEmpty();

        var result = new NativeEntryArguments(tokens).Read(parsed, command);
        var captured = NativeTemplateCommandInput.CaptureEntry(command, parsed, result.Tokens,
            new HashSet<Option> { command.TemplateOption, command.NameOption, command.FormatOption,
                command.ForceOption, command.NonInteractiveOption, command.ListOption }, result.PathIndex);

        result.Directory.Info.FullName.Should().Be(Path.GetFullPath("project"));
        captured.OriginalTokens.Should().Equal(result.Tokens);
        captured.NativeTokens.Should().Equal(tokens.Where(token => token.StartsWith("--greeting", StringComparison.Ordinal))
            .SelectMany<string, string>(token => token.Contains('=') ? [token] : [token, tokens[Array.IndexOf(tokens, token) + 1]]));
    }

    [Theory]
    [InlineData("--param:Auth")]
    [InlineData("-p:n")]
    public void Read_FallbackAliasColonIsNotAnInlineValueSeparator(string alias)
    {
        NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
        command.PathArgument!.CustomParser = _ => WorkingDirectory.FromCwd();
        FuncRootCommand root = [command];
        string[] tokens = ["new", alias, "original", "project", "-t", "source"];
        var parsed = root.Parse(tokens, new() { EnablePosixBundling = false, ResponseFileTokenReplacer = null });

        var result = new NativeEntryArguments(tokens).Read(parsed, command);
        var captured = NativeTemplateCommandInput.CaptureEntry(command, parsed, result.Tokens,
            new HashSet<Option> { command.TemplateOption }, result.PathIndex);

        result.Directory.Info.FullName.Should().Be(Path.GetFullPath("project"));
        captured.NativeTokens.Should().Equal(alias, "original");
    }

    [Theory]
    [InlineData("en-US", "-1", "before")]
    [InlineData("en-US", "-1", "inline")]
    [InlineData("en-US", "-1", "pathFirst")]
    [InlineData("en-US", "-1.25", "before")]
    [InlineData("en-US", "-1.25", "inline")]
    [InlineData("en-US", "-1.25", "pathFirst")]
    [InlineData("fr-FR", "-1,25", "before")]
    [InlineData("fr-FR", "-1,25", "inline")]
    [InlineData("fr-FR", "-1,25", "pathFirst")]
    [InlineData("fr-FR", "-1.25", "before")]
    [InlineData("en-US", "-1e-3", "before")]
    [InlineData("fr-FR", "-1e-3", "before")]
    public void Read_SignedNumericScalar_PreservesExplicitPathAndParameterProvenance(string culture, string value, string layout)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            const string alias = "--queue-trigger-name";
            string[] scalar = layout == "inline" ? [alias + "=" + value] : [alias, value];
            string[] tail = layout == "pathFirst" ? ["project", .. scalar] : [.. scalar, "project"];
            string[] tokens = ["new", .. tail, "-t", "QueueTrigger-JavaScript", "--non-interactive", "--format=json"];
            string[] original = [.. tokens];
            NewCommand command = new(Substitute.For<INewCommandRunner>(), Substitute.For<INewCommandTemplateOptionProvider>());
            command.PathArgument!.CustomParser = _ => WorkingDirectory.FromCwd();
            FuncRootCommand root = [command];
            var parsed = root.Parse(tokens, new() { EnablePosixBundling = false, ResponseFileTokenReplacer = null });
            TemplateParameterDefinition parameter = new("queueTrigger-name", "queue-trigger-name", null, "Queue name",
                "string", [], null, false, false, false);
            TemplateArgumentParser parser = new();
            var accepted = parser.Parse(new("Queue", [parameter], new HashSet<string>(), scalar));

            parsed.Errors.Should().BeEmpty();
            accepted.IsComplete.Should().BeTrue();
            var result = new NativeEntryArguments(tokens).Read(parsed, command);
            var captured = NativeTemplateCommandInput.CaptureEntry(command, parsed, result.Tokens,
                new HashSet<Option> { command.TemplateOption, command.NameOption, command.FormatOption,
                    command.ForceOption, command.NonInteractiveOption, command.ListOption }, result.PathIndex);

            result.PathIndex.Should().Be(layout == "pathFirst" ? 0 : scalar.Length);
            result.Directory.Info.FullName.Should().Be(Path.GetFullPath("project"));
            captured.OriginalTokens.Should().Equal(tokens.Skip(1));
            captured.NativeTokens.Should().Equal(scalar);
            var bound = parser.Parse(new("Queue", [parameter], captured.ReservedAliases, captured.NativeTokens));
            bound.ValuesByCanonicalName.Should().Contain("queueTrigger-name", value);
            bound.ExplicitParameterNames.Should().Equal("queueTrigger-name");
            var invalid = parser.Parse(new("Queue", [parameter with { DataType = "bool" }],
                captured.ReservedAliases, captured.NativeTokens));
            captured.RestoreProvenance(invalid).InvalidInputDiagnostics.Should().ContainSingle().Which.TokenIndices
                .Should().Equal(layout == "inline" ? [0] : [0, 1]);
            tokens.Should().Equal(original);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
