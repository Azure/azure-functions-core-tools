// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Unit tests for the <c>func.host.json</c> reader: option aliases, hidden
/// symbols, symbol validators, the function-name validator, and tolerance of
/// malformed input.
/// </summary>
public class FuncHostFileParserTests
{
    private const string Sample =
        """
        {
          "symbolInfo": [
            { "id": "AuthLevel", "longName": "auth-level" },
            { "id": "AppObject", "isHidden": true },
            { "id": "Route", "longName": "route", "validator": { "expression": "^[a-z]+$", "errorText": "letters only" } }
          ],
          "functionName": {
            "validator": { "expression": "^[A-Za-z][A-Za-z0-9_]*$", "errorText": "invalid name" }
          }
        }
        """;

    [Fact]
    public void Parse_ReadsLongNameAlias()
    {
        FuncHostFile host = FuncHostFileParser.Parse(Sample);

        host.FindSymbol("AuthLevel").Should().NotBeNull();
        host.FindSymbol("AuthLevel")!.LongName.Should().Be("auth-level");
    }

    [Fact]
    public void Parse_ReadsHiddenFlag()
    {
        FuncHostFile host = FuncHostFileParser.Parse(Sample);

        host.FindSymbol("AppObject")!.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void Parse_ReadsSymbolValidator()
    {
        FuncHostFile host = FuncHostFileParser.Parse(Sample);

        FuncHostValidator? validator = host.FindSymbol("Route")!.Validator;
        validator.Should().NotBeNull();
        validator!.Expression.Should().Be("^[a-z]+$");
        validator.ErrorText.Should().Be("letters only");
    }

    [Fact]
    public void Parse_ReadsFunctionNameValidator()
    {
        FuncHostFile host = FuncHostFileParser.Parse(Sample);

        host.FunctionNameValidator.Should().NotBeNull();
        host.FunctionNameValidator!.Expression.Should().Be("^[A-Za-z][A-Za-z0-9_]*$");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not valid json")]
    [InlineData("[]")]
    public void Parse_MalformedOrEmpty_ReturnsEmpty(string? json)
    {
        FuncHostFile host = FuncHostFileParser.Parse(json);

        host.Symbols.Should().BeEmpty();
        host.FunctionNameValidator.Should().BeNull();
    }
}
