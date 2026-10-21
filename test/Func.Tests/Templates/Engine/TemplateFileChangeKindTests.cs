// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateFileChangeKindTests
{
    [Fact]
    public void Kind_InternalContract_IsAnEnum()
    {
        typeof(TemplateFileChange).GetProperty(nameof(TemplateFileChange.Kind))!.PropertyType.IsEnum.Should().BeTrue();
    }

    [Theory]
    [InlineData(ChangeKind.Create, "Create")]
    [InlineData(ChangeKind.Change, "Change")]
    [InlineData(ChangeKind.Overwrite, "Overwrite")]
    [InlineData(ChangeKind.Delete, "Delete")]
    public void FromNative_KnownKind_MapsByMeaningAndPreservesItsDisplayName(ChangeKind native, string literal)
    {
        var change = TemplateFileChange.FromNative("result.txt", native);

        change.Path.Should().Be("result.txt");
        change.Kind.Should().Be(Enum.Parse<TemplateFileChangeKind>(literal));
        change.GetKindName().Should().Be(literal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void FromNative_UnknownKind_Throws(int value)
    {
        Action convert = () => TemplateFileChange.FromNative("result.txt", (ChangeKind)value);

        convert.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("kind");
    }

    [Fact]
    public void GetKindName_UnknownInternalKind_Throws()
    {
        var change = new TemplateFileChange("result.txt", (TemplateFileChangeKind)999);

        Action display = () => change.GetKindName();

        display.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("Kind");
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Change")]
    [InlineData("Overwrite")]
    [InlineData("Delete")]
    public void Serialize_KnownKind_PreservesExactStringsAndPropertyOrder(string kind)
    {
        var change = CreateChange(kind);

        string json = JsonSerializer.Serialize(change);
        string camelCase = JsonSerializer.Serialize(change, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        string result = JsonSerializer.Serialize(new TemplateInvocationResult(TemplateInvocationStatus.Success, [change]));
        string committed = JsonSerializer.Serialize(new TemplateInvocationResult(TemplateInvocationStatus.Success, [change]) { HasCommittedOutput = true });

        json.Should().Be("{\"Path\":\"result.txt\",\"Kind\":\"" + kind + "\"}");
        camelCase.Should().Be("{\"path\":\"result.txt\",\"kind\":\"" + kind + "\"}");
        result.Should().Be("{\"Status\":0,\"Changes\":[{\"Path\":\"result.txt\",\"Kind\":\"" + kind + "\"}],\"Diagnostic\":null}");
        committed.Should().Be(result);
        change.ToString().Should().Be("TemplateFileChange { Path = result.txt, Kind = " + kind + " }");
    }

    [Fact]
    public void Serialize_UnknownInternalKind_FailsRatherThanWritingAnIntegerOrUnknownText()
    {
        var change = CreateChange("999");

        Action serialize = () => JsonSerializer.Serialize(change);

        serialize.Should().Throw<JsonException>();
    }

    private static TemplateFileChange CreateChange(string kind)
    {
        Type type = typeof(TemplateFileChange).GetProperty(nameof(TemplateFileChange.Kind))!.PropertyType;
        object value = type == typeof(string) ? kind : Enum.Parse(type, kind);
        return (TemplateFileChange)Activator.CreateInstance(typeof(TemplateFileChange), "result.txt", value)!;
    }
}
