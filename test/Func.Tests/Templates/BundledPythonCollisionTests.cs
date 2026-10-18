// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Fact]
    public async Task SelectedAppend_RealDaprFragmentHasConstantSecondaryName_RefusesDespiteDistinctRequestedName()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("DaprPublishOutputBinding-Python", TemplateType.Item)).Group!.Single();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "dapr-collision"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        const string original = "@app.function_name('DaprTopicTriggerFuncApp')\n@app.route(route='old')\ndef DifferentMethod(req):\n    pass\n";
        File.WriteAllText(app, original);
        var before = File.ReadAllBytes(app);
        PythonSelectedItemEffects effects = new(new PhysicalFileSystem(), new PythonTemplateSourceReader(),
            new Azure.Functions.Cli.Templates.Python.Shared.PythonTemplateAppendExecutor(), confirmed: true);
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);
        var parameters = selected.Parameters.ToDictionary(parameter => parameter.CanonicalName, parameter => parameter.DefaultValue);

        var result = await effects.InvokeAsync(templater, selected, binding, new(output, parameters, "DistinctDaprMain"), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Effects!.Kind.Should().Be("preflightFailed");
        result.Diagnostic.Should().Contain("'DaprTopicTriggerFuncApp' already exists");
        File.ReadAllBytes(app).Should().Equal(before);
    }

    [Theory]
    [InlineData(false, false, "def Greeting(req):\n    pass\n")]
    [InlineData(true, false, "def Greeting(req):\n    pass\n")]
    [InlineData(false, true, "async def Greeting(req):\n    pass\n")]
    [InlineData(true, true, "@app.function_name(name='Greeting')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData(false, false, "@app.function_name('Greeting')\n@app.route(route='old')\ndef different(req):\n    pass\n")]
    [InlineData(false, false, "def Different(req):\n    pass\napp.function_name('Greeting')(app.route(route='old')(Different))\n")]
    public async Task SelectedAppend_ExistingFunctionCollision_PreviewAndForceRefuseWithoutWrites(
        bool preview, bool force, string original)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "collision"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(app, original);
        var before = File.ReadAllBytes(app);
        PythonSelectedItemEffects effects = new(new PhysicalFileSystem(), new PythonTemplateSourceReader(),
            new Azure.Functions.Cli.Templates.Python.Shared.PythonTemplateAppendExecutor(), confirmed: true);
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);

        var result = await effects.InvokeAsync(templater, selected, binding, new(output,
            new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "FUNCTION" }, "Greeting", preview, force), CancellationToken.None);

        result!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        result.Effects!.Kind.Should().Be("preflightFailed");
        result.Effects.CompletedEffects.Should().BeEmpty();
        result.Diagnostic.Should().Contain("'Greeting' already exists").And.Contain("Choose a different function name");
        File.ReadAllBytes(app).Should().Equal(before);
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
    }

    [Fact]
    public async Task SelectedAppend_RepeatedDistinctThenDuplicateNames_PreservesSourceAndRefusesFreshDuplicate()
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "repeat"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        const string original = "import logging\nimport azure.functions as func\napp = func.FunctionApp()\n"
            + "# def First(req):\ntext = '''@app.function_name('Second')\\ndef Second(req):'''\n";
        File.WriteAllText(app, original);
        PythonSelectedItemEffects effects = new(new PhysicalFileSystem(), new PythonTemplateSourceReader(),
            new Azure.Functions.Cli.Templates.Python.Shared.PythonTemplateAppendExecutor(), confirmed: true);
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "FUNCTION" }, "First");
        foreach (var name in new[] { "First", "Second" })
        {
            var created = await effects.InvokeAsync(templater, selected, binding, request with { Name = name }, CancellationToken.None);
            created!.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        }

        var before = File.ReadAllBytes(app);
        var duplicate = await effects.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        duplicate!.Status.Should().Be(TemplateInvocationStatus.EffectFailure);
        File.ReadAllBytes(app).Should().Equal(before);
        File.ReadAllText(app).Should().StartWith(original).And.Contain("def First(", "def Second(");
    }

    [Theory]
    [InlineData("bp = func.Blueprint()\n@bp.route(route='old')\ndef ExistingBlueprint(req):\n    pass\napp.register_functions(bp)\n")]
    [InlineData("NAME = 'ExistingPublic'\n@app.function_name(NAME)\n@app.route(route='old')\ndef DifferentMethod(req):\n    pass\n")]
    [InlineData("def Different(timer):\n    pass\napp.timer_trigger(arg_name='timer', schedule='0 * * * * *')(Different)\n")]
    [InlineData("@app.route(route='old')\ndef Alpha\u200cOther(req):\n    pass\n")]
    [InlineData("def Different(req):\n    pass\nif False: app.function_name('Greeting')(app.route(route='old')(Different))\n")]
    [InlineData("def Different(req):\n    pass\ndef factory(): return app.function_name('Greeting')(app.route(route='old')(Different))\n")]
    [InlineData("def Different(req):\n    pass\ncreated = app.function_name('Greeting')(app.route(route='old')(Different)) if False else None\n")]
    [InlineData("def Different(req):\n    pass\n@app.function_name('Inner')\n@app.route(route='old')\ndef Different(req):\n    pass\napp.function_name('Greeting')(Different)\n")]
    [InlineData("alias = app\nalias = (app)\ndef Different(timer):\n    pass\nalias.timer_trigger(arg_name='timer', schedule='0 * * * * *')(Different)\n")]
    [InlineData("def register_user_handler(target):\n    def DynamicExisting(req):\n        pass\n"
        + "    target.route(route='existing')(DynamicExisting)\nregister_user_handler(app)\n")]
    public async Task SelectedAppend_PartialNameAnalysis_PreviewAndApplySucceedWithValidationLimit(string original)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var selected = (await templater.ResolveGroupAsync("HttpTrigger-Python", TemplateType.Item)).Group!.Single();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "partial"));
        Directory.CreateDirectory(output.Info.FullName);
        var app = Path.Combine(output.Info.FullName, "function_app.py");
        File.WriteAllText(app, original);
        var before = File.ReadAllBytes(app);
        PythonSelectedItemEffects effects = new(new PhysicalFileSystem(), new PythonTemplateSourceReader(),
            new Azure.Functions.Cli.Templates.Python.Shared.PythonTemplateAppendExecutor(), confirmed: true);
        var binding = await effects.ResolveAsync(selected, CancellationToken.None);
        var request = new TemplateInvocationRequest(output,
            new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "FUNCTION" }, "Greeting", DryRun: true);

        var preview = await effects.InvokeAsync(templater, selected, binding, request, CancellationToken.None);

        preview!.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Diagnostic.Should().Contain("validation is partial").And.Contain("Existing Python was not executed");
        preview.Effects!.Kind.Should().Be("previewed");
        File.ReadAllBytes(app).Should().Equal(before);

        var created = await effects.InvokeAsync(templater, selected, binding, request with { DryRun = false }, CancellationToken.None);

        created!.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.Diagnostic.Should().Contain("validation is partial").And.Contain("Verify application indexing");
        created.Effects!.Kind.Should().Be("applied");
        File.ReadAllBytes(app).Take(before.Length).Should().Equal(before);
        File.ReadAllText(app).Should().Contain("def Greeting(");
    }
}
