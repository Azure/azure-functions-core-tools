// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.V2;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Theory]
    [InlineData("preview", "OpenAI-Assistant-Python", null, "OpenAI-Assistant-Python", "OpenAI-Assistant-Python")]
    [InlineData("experimental", "OpenAI-Assistant-Python", null, "OpenAI-Assistant-Python", "OpenAI-Assistant-Python")]
    [InlineData("preview", "OpenAI-Chat-Python", null, "OpenAI-Chat-Python", "OpenAI-Chat-Python")]
    [InlineData("experimental", "OpenAI-Chat-Python", null, "OpenAI-Chat-Python", "OpenAI-Chat-Python")]
    [InlineData("preview", "OpenAI-RAG-Python", null, "OpenAI-RAG-Python", "OpenAI-RAG-Python")]
    [InlineData("experimental", "OpenAI-RAG-Python", null, "OpenAI-RAG-Python", "OpenAI-RAG-Python")]
    [InlineData("preview", "OpenAI-Assistant-Python", "CustomName", "OpenAI-Assistant-Python", "CustomName")]
    [InlineData("experimental", "OpenAI-Assistant-Python", "CustomName", "OpenAI-Assistant-Python", "CustomName")]
    [InlineData("preview", "OpenAI-Chat-Python", "CustomName", "OpenAI-Chat-Python", "CustomName")]
    [InlineData("experimental", "OpenAI-Chat-Python", "CustomName", "OpenAI-Chat-Python", "CustomName")]
    [InlineData("preview", "OpenAI-RAG-Python", "CustomName", "OpenAI-RAG-Python", "CustomName")]
    [InlineData("experimental", "OpenAI-RAG-Python", "CustomName", "OpenAI-RAG-Python", "CustomName")]
    [InlineData("stable", "HttpTrigger-Python", null, "http_trigger", "http_trigger")]
    [InlineData("stable", "HttpTrigger-Python", "CustomName", "http_trigger", "CustomName")]
    public async Task DirectDefault_ActualPackagedReaderCompatibilityNativeAndAppend_PreserveLiteralNamesAndRawBodies(
        string channel, string id, string? requestedName, string authoredDefault, string expectedName)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync(channel));
        var selected = (await templater.ResolveGroupAsync(id, TemplateType.Item)).Group!.Single();
        var reader = new PythonPolicyTestReader();
        var source = await reader.ReadAsync(selected, CancellationToken.None);
        var mapping = source.Binding.Append!;
        var helper = (await templater.ResolveGroupAsync(mapping.HelperIdentity, TemplateType.Item)).Group!.Single();
        var helperSource = await reader.ReadAsync(helper, CancellationToken.None);
        var provider = CreatePythonProvider();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        var raw = _sources.Single(template => template.Id == id);
        var rawBodies = raw.Files!.ToDictionary(pair => pair.Key, pair => pair.Value);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "direct-native"));

        var compatibility = await provider.Compatibility.ResolveAsync(selected, arguments, requestedName, false, CancellationToken.None);

        compatibility.Succeeded.Should().BeTrue(string.Join(" ", compatibility.Diagnostics));
        compatibility.Name.Should().Be(expectedName);
        compatibility.Origin.Should().Be(requestedName is null ? "authoredDefault" : "explicit");
        source.Compatibility!.DefaultName.Should().Be(authoredDefault);
        selected.Entry.DefaultName.Should().Be(authoredDefault);
        helper.Entry.DefaultName.Should().Be(authoredDefault);
        helperSource.Binding.InternalExpansionOnly.Should().BeTrue();
        helperSource.Compatibility.Should().BeNull();
        helper.Entry.Package.Should().Be(selected.Entry.Package);
        helperSource.HelperSource.Should().Be(source.HelperSource);
        mapping.CreateTargetFileName.Should().Be("function_app.py");
        mapping.AppendTargetFileName.Should().Be("function_app.py");

        var invocation = new TemplateInvocationRequest(output, arguments.ValuesByCanonicalName, compatibility.Name, DryRun: true);
        var preview = await selected.InvokeAsync(invocation, CancellationToken.None);
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
        var created = await selected.InvokeAsync(invocation with { DryRun = false }, CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        Directory.GetFiles(output.Info.FullName).Select(Path.GetFileName).Should().Equal("function_app.py");
        var values = arguments.ValuesByCanonicalName.ToDictionary(pair => pair.Key, pair => pair.Value);
        values["trigger-functionName"] = expectedName;
        var legacy = Directory.CreateDirectory(Path.Combine(_root, "direct-legacy-create"));
        new V2TemplateEngine().Apply(raw, expectedName, values, legacy, false)
            .Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllBytes(Path.Combine(output.Info.FullName, "function_app.py")).Should()
            .Equal(File.ReadAllBytes(Path.Combine(legacy.FullName, "function_app.py")));

        var existing = WorkingDirectory.FromExplicit(Path.Combine(_root, "direct-append"));
        Directory.CreateDirectory(existing.Info.FullName);
        var app = Path.Combine(existing.Info.FullName, "function_app.py");
        const string prefix = "import azure.functions as func\napp = func.FunctionApp()\n# user-owned prefix without a trailing newline";
        File.WriteAllText(app, prefix);
        var binding = await provider.Effects.ResolveAsync(selected, CancellationToken.None);
        binding.Diagnostics.Should().BeEmpty();
        binding.Append.Should().Be(mapping);
        var append = invocation with { OutputDirectory = existing };
        var appendPreview = await provider.Effects.InvokeAsync(templater, selected, binding, append, CancellationToken.None);
        appendPreview!.Status.Should().Be(TemplateInvocationStatus.Success, appendPreview.Diagnostic);
        appendPreview.Effects!.Kind.Should().Be("previewed");
        File.ReadAllText(app).Should().Be(prefix);
        var applied = await provider.Effects.InvokeAsync(templater, selected, binding, append with { DryRun = false }, CancellationToken.None);
        applied!.Status.Should().Be(TemplateInvocationStatus.Success, applied.Diagnostic);
        applied.Effects!.Kind.Should().Be("applied");
        var legacyAppend = Directory.CreateDirectory(Path.Combine(_root, "direct-legacy-append"));
        File.WriteAllText(Path.Combine(legacyAppend.FullName, "function_app.py"), prefix);
        new V2TemplateEngine().Apply(raw, expectedName, values, legacyAppend, false)
            .Should().BeOfType<TemplateApplicationResult.Created>();
        File.ReadAllBytes(app).Should().Equal(File.ReadAllBytes(Path.Combine(legacyAppend.FullName, "function_app.py")));
        File.ReadAllText(app).Should().StartWith(prefix + Environment.NewLine);
        Directory.GetFiles(existing.Info.FullName).Select(Path.GetFileName).Should().Equal("function_app.py");
        raw.Files.Should().BeEquivalentTo(rawBodies);
    }
}
