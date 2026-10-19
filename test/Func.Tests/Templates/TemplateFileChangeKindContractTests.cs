// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class TemplateFileChangeKindContractTests
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
    public void FromNative_KnownKinds_PreserveTheExactNativeSpelling(ChangeKind native, string literal)
    {
        var change = TemplateFileChange.FromNative("result.txt", native);

        change.Path.Should().Be("result.txt");
        change.Kind.Should().Be(Enum.Parse<TemplateFileChangeKind>(literal));
        change.GetKindName().Should().Be(literal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public void FromNative_UnknownKinds_RefuseInsteadOfInventingAString(int value)
    {
        Action create = () => TemplateFileChange.FromNative("result.txt", (ChangeKind)value);

        create.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("kind");
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Change")]
    [InlineData("Overwrite")]
    [InlineData("Delete")]
    [InlineData("Append")]
    [InlineData("Preserve")]
    public void Render_KnownKinds_RetainExactNewAndInitJsonStringsAndArrayShapes(string kind)
    {
        TemplateFileChange change = CreateChange(kind);
        var itemOutput = new TestInteractionService();
        var projectOutput = new TestInteractionService();

        new NewExecutionRenderer(itemOutput).Render(new("control", true)
        {
            Invocation = new(TemplateInvocationStatus.Success, [change]),
        }, jsonOutput: true).Should().Be(0);
        new InitExecutionRenderer(projectOutput).Render(new("control", true)
        {
            Preview = new(TemplateInvocationStatus.Success, [change]),
            Invocation = new(TemplateInvocationStatus.Success, [change]),
        }, jsonOutput: true).Should().Be(0);

        using var item = ReadJson(itemOutput);
        using var project = ReadJson(projectOutput);
        string expected = "[{\"path\":\"result.txt\",\"kind\":\"" + kind + "\"}]";
        item.RootElement.GetProperty("invocation").GetProperty("changes").GetRawText().Should().Be(expected);
        var invocation = project.RootElement.GetProperty("invocation");
        invocation.GetProperty("plannedChanges").GetRawText().Should().Be(expected);
        invocation.GetProperty("completedChanges").GetRawText().Should().Be(expected);
        invocation.EnumerateObject().Select(property => property.Name).Should()
            .Equal("status", "plannedChanges", "completedChanges", "plannedConfigurations", "configuredProjects");
    }

    [Fact]
    public void Render_AppendWithTypedEffects_RetainsTheSameStringProjection()
    {
        NativeTemplateEffectResult effects = new("previewed", true, new(WorkingDirectory.FromExplicit(Path.GetTempPath()), []), [], [], []);
        var output = new TestInteractionService();

        new NewExecutionRenderer(output).Render(new("control", true)
        {
            Invocation = new(TemplateInvocationStatus.Success, [CreateChange("Append")]) { Effects = effects },
        }, jsonOutput: true).Should().Be(0);

        using var result = ReadJson(output);
        var invocation = result.RootElement.GetProperty("invocation");
        invocation.GetProperty("changes").GetRawText().Should().Be("""[{"path":"result.txt","kind":"Append"}]""");
        invocation.GetProperty("effects").GetProperty("kind").GetString().Should().Be("previewed");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Render_UnknownInternalKind_FailsLoudlyWithoutReportingSuccess(bool project, bool json)
    {
        TemplateFileChange change = CreateChange("999");
        var output = new TestInteractionService();
        Action render = project
            ? () => new InitExecutionRenderer(output).Render(new("control", true)
            {
                Preview = new(TemplateInvocationStatus.Success, [change]),
                Invocation = new(TemplateInvocationStatus.Success, [change]),
            }, json)
            : () => new NewExecutionRenderer(output).Render(new("control", true)
            {
                Invocation = new(TemplateInvocationStatus.Success, [change]),
            }, json);

        render.Should().Throw<ArgumentOutOfRangeException>();
        output.Lines.Should().NotContain(line => line.StartsWith("JSON:", StringComparison.Ordinal) || line.Contains("999", StringComparison.Ordinal));
    }

    private static TemplateFileChange CreateChange(string kind)
    {
        Type type = typeof(TemplateFileChange).GetProperty(nameof(TemplateFileChange.Kind))!.PropertyType;
        object value = type == typeof(string) ? kind : Enum.Parse(type, kind);
        return (TemplateFileChange)Activator.CreateInstance(typeof(TemplateFileChange), "result.txt", value)!;
    }

    private static JsonDocument ReadJson(TestInteractionService interaction) =>
        JsonDocument.Parse(interaction.Lines.Single(line => line.StartsWith("JSON: ", StringComparison.Ordinal))["JSON: ".Length..]);
}
