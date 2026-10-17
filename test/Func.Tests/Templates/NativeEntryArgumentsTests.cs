// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
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
            new HashSet<Option> { command.TemplateOption, command.NameOption, command.OutputOption,
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
}
