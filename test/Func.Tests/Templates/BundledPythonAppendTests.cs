// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.V2;
using Azure.Functions.Cli.Workloads.Python;
using NuGet.Packaging;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    [Fact]
    public async Task AppendHelpers_ExactMappedInventory_IsHiddenAndRetainsOriginalNativeExpansionMetadata()
    {
        await InstallAsync();
        using var archive = new PackageArchiveReader(_archive);
        using var templater = CreateTemplater(await ReadBundleAsync("preview"));
        var entries = await templater.ListAsync(TemplateType.Item);

        entries.Where(entry => _helperIdentities.Contains(entry.Identity)).Should().HaveCount(25).And.OnlyContain(entry => entry.IsHidden);
        entries.Where(entry => !_helperIdentities.Contains(entry.Identity)).Should().HaveCount(25);
        foreach (var application in Applications.EnumerateArray())
        {
            var helper = application.GetProperty("appendHelper");
            var original = JsonNode.Parse(Read(archive, application.GetProperty("configuration").GetString()!))!;
            var configuration = JsonNode.Parse(Read(archive, helper.GetProperty("configuration").GetString()!))!;
            foreach (var property in new[] { "symbols", "forms", "constraints", "defaultName", "name" })
            {
                JsonNode.DeepEquals(configuration[property], original[property]).Should().BeTrue(
                    $"{application.GetProperty("templateIdentity").GetString()} must share the native {property} contract");
            }

            configuration["tags"]!["type"]!.GetValue<string>().Should().Be("item");
            configuration["tags"]!["language"]!.GetValue<string>().Should().Be("Python");
            configuration["identity"]!.GetValue<string>().Should().Be(helper.GetProperty("templateIdentity").GetString());
            var originalEntry = entries.Single(entry => entry.Identity == application.GetProperty("templateIdentity").GetString());
            var helperEntry = entries.Single(entry => entry.Identity == helper.GetProperty("templateIdentity").GetString());
            helperEntry.Parameters.Should().BeEquivalentTo(originalEntry.Parameters);
        }
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("preview")]
    [InlineData("experimental")]
    public async Task AppendHelpers_AllPinnedRecords_RealNativeExpansionAndOwnedAppendMatchLegacyWithoutReplacingUserBytes(string channel)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync(channel));
        var entries = await templater.ListAsync(TemplateType.Item);
        var ids = _channels.RootElement.EnumerateArray().Single(entry => entry.GetProperty("channel").GetString() == channel)
            .GetProperty("templateIds").EnumerateArray().Select(id => id.GetString()!).ToList();
        const string original = "import datetime\nimport json\nimport logging\nimport azure.functions as func\napp = func.FunctionApp()\n@app.route(route='existing')\ndef existing(req):\n    return func.HttpResponse('user code')";
        foreach (var id in ids)
        {
            var source = _sources.Single(source => source.Id == id);
            var entry = entries.Single(entry => entry.ShortNames.Contains(id));
            var application = Applications.EnumerateArray().Single(item => item.GetProperty("templateIdentity").GetString() == entry.Identity);
            var helper = application.GetProperty("appendHelper");
            var selected = (await templater.ResolveGroupAsync(helper.GetProperty("templateIdentity").GetString()!, TemplateType.Item)).Group!.Single();
            var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing-native", id));
            Directory.CreateDirectory(output.Info.FullName);
            var app = Path.Combine(output.Info.FullName, "function_app.py");
            File.WriteAllText(app, original);
            var before = File.ReadAllBytes(app);
            var digest = Convert.ToHexStringLower(SHA256.HashData(before));
            var createTarget = application.GetProperty("createTarget").GetString()!;
            if (createTarget != "function_app.py")
            {
                File.WriteAllText(Path.Combine(output.Info.FullName, createTarget), "legacy create-target marker");
            }

            var values = entry.Parameters.ToDictionary(parameter => parameter.CanonicalName, parameter => parameter.DefaultValue, StringComparer.Ordinal);
            var name = "Greeting_9";
            var expanded = WorkingDirectory.FromExplicit(Path.Combine(_root, "expanded", id));
            var plan = await new PythonTemplateApplicationPlanner().PlanAsync(output.Info.FullName, createTarget, true, digest,
                async (mode, token) =>
                {
                    mode.Should().Be(PythonTemplateApplicationMode.AppendToFile);
                    var request = new TemplateInvocationRequest(expanded, values, name, DryRun: true);
                    var preview = await selected.InvokeAsync(request, token);
                    preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
                    Directory.Exists(expanded.Info.FullName).Should().BeFalse();
                    var result = await selected.InvokeAsync(request with { DryRun = false }, token);
                    result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
                    result.Changes.Should().ContainSingle().Which.Kind.Should().Be("Create");
                    var path = Path.Combine(expanded.Info.FullName, helper.GetProperty("expectedOutput").GetString()!);
                    Directory.GetFiles(expanded.Info.FullName, "*", SearchOption.AllDirectories).Should().ContainSingle().Which.Should().Be(path);
                    File.GetAttributes(path).HasFlag(FileAttributes.Directory).Should().BeFalse();
                    return await File.ReadAllTextAsync(path, token);
                }, CancellationToken.None);

            await new PythonTemplateAppendExecutor().AppendAsync(plan.TargetPath, plan.Content, plan.ExpectedTargetSha256!, CancellationToken.None);

            File.ReadAllBytes(app).Take(before.Length).Should().Equal(before);
            var legacy = Directory.CreateDirectory(Path.Combine(_root, "existing-legacy", id));
            File.WriteAllText(Path.Combine(legacy.FullName, "function_app.py"), original);
            var legacyCreateTarget = application.GetProperty("legacyCreateTarget").GetString()!;
            if (legacyCreateTarget != "function_app.py")
            {
                File.WriteAllText(Path.Combine(legacy.FullName, legacyCreateTarget), "legacy create-target marker");
            }

            values["trigger-functionName"] = name;
            new V2TemplateEngine().Apply(source, name, values, legacy, false).Should().BeOfType<TemplateApplicationResult.Created>();
            File.ReadAllBytes(app).Should().Equal(File.ReadAllBytes(Path.Combine(legacy.FullName, "function_app.py")), id);
            Directory.GetFiles(output.Info.FullName).Should().HaveCount(createTarget == "function_app.py" ? 1 : 2);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppendHelpers_ChangedTargetOrCanceledAfterRealExpansion_PreservesUserEdits(bool cancel)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var mapping = Applications.EnumerateArray().Single(item => item.GetProperty("templateIdentity").GetString() == "Azure.Functions.Python.HttpTrigger");
        var helper = mapping.GetProperty("appendHelper");
        var selected = (await templater.ResolveGroupAsync(helper.GetProperty("templateIdentity").GetString()!, TemplateType.Item)).Group!.Single();
        var root = Path.Combine(_root, "existing-app");
        Directory.CreateDirectory(root);
        var app = Path.Combine(root, "function_app.py");
        File.WriteAllText(app, "user code");
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(app)));
        var expanded = WorkingDirectory.FromExplicit(Path.Combine(_root, "expanded"));
        var created = await selected.InvokeAsync(new(expanded,
            new Dictionary<string, string?> { ["httpTrigger-authLevel"] = "ANONYMOUS" }, "Greeting"), CancellationToken.None);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        var snippet = File.ReadAllText(Path.Combine(expanded.Info.FullName, "function_body.py"));
        using CancellationTokenSource cancellation = new();
        if (cancel)
        {
            cancellation.Cancel();
        }
        else
        {
            File.WriteAllText(app, "new user edits");
        }

        Func<Task> append = () => new PythonTemplateAppendExecutor().AppendAsync(app, snippet, digest, cancellation.Token);

        if (cancel)
        {
            await append.Should().ThrowAsync<OperationCanceledException>();
            File.ReadAllText(app).Should().Be("user code");
        }
        else
        {
            await append.Should().ThrowAsync<InvalidOperationException>();
            File.ReadAllText(app).Should().Be("new user edits");
        }

        Directory.GetFiles(root).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppendHelpers_InvalidArgumentOrPreCanceledNativeExpansion_DoesNotProduceSnippetOrChangeApp(bool cancel)
    {
        await InstallAsync();
        using var templater = CreateTemplater(await ReadBundleAsync("stable"));
        var mapping = Applications.EnumerateArray().Single(item => item.GetProperty("templateIdentity").GetString() == "Azure.Functions.Python.HttpTrigger");
        var helper = mapping.GetProperty("appendHelper");
        var selected = (await templater.ResolveGroupAsync(helper.GetProperty("templateIdentity").GetString()!, TemplateType.Item)).Group!.Single();
        var root = Path.Combine(_root, "existing-app");
        Directory.CreateDirectory(root);
        var app = Path.Combine(root, "function_app.py");
        File.WriteAllText(app, "user code");
        var expanded = WorkingDirectory.FromExplicit(Path.Combine(_root, "expanded"));
        using CancellationTokenSource cancellation = new();
        if (cancel)
        {
            cancellation.Cancel();
        }

        var request = new TemplateInvocationRequest(expanded,
            new Dictionary<string, string?> { ["httpTrigger-authLevel"] = cancel ? "FUNCTION" : "UNSUPPORTED" }, "Greeting");
        if (cancel)
        {
            Func<Task> invoke = () => selected.InvokeAsync(request, cancellation.Token);
            await invoke.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await selected.InvokeAsync(request, cancellation.Token);
            result.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        }

        Directory.Exists(expanded.Info.FullName).Should().BeFalse();
        File.ReadAllText(app).Should().Be("user code");
        Directory.GetFiles(root).Should().ContainSingle();
    }
}
