// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using NuGet.Packaging;
using RunnableComponents = Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Fact]
    public void AuthoredLayout_RealArchive_AllItemsDeclareSourceOwnedTargetsWithoutNewUserParameters()
    {
        using var inner = ReadNodeArchive();
        foreach (var template in ReadNodeSource().Templates)
        {
            using var document = JsonDocument.Parse(Read(inner, NodeConfigurationPath(template.Id!)));
            var config = document.RootElement;
            var intent = config.GetProperty("symbols").GetProperty("InvocationOutputIntent");
            intent.GetProperty("type").GetString().Should().Be("bind");
            intent.GetProperty("binding").GetString().Should().Be("host:func:item-output-intent");
            var sources = config.GetProperty("sources").EnumerateArray().ToArray();
            sources.Should().HaveCount(2);
            string rawPath = template.Actions!.Single(action => action.Type == "WriteToFile").FilePath!;
            sources[0].GetProperty("target").GetString().Should().Be(rawPath[..(rawPath.LastIndexOf('/') + 1)]);
            sources[0].GetProperty("condition").GetString().Should().Be("(InvocationOutputIntent == \"legacy\")");
            sources[1].GetProperty("target").GetString().Should().Be("./");
            sources[1].GetProperty("condition").GetString().Should().Be("(InvocationOutputIntent != \"legacy\")");
            sources.Should().OnlyContain(source => source.GetProperty("source").GetString() == "./");
        }
    }

    [Theory]
    [InlineData("JavaScript", ".js", null)]
    [InlineData("TypeScript", ".ts", null)]
    [InlineData("JavaScript", ".js", "literal")]
    [InlineData("TypeScript", ".ts", "literal")]
    [InlineData("JavaScript", ".js", "legacy")]
    [InlineData("TypeScript", ".ts", "legacy")]
    public async Task AuthoredLayout_ActualNativeBackend_UsesTemplateMappingsAndMissingIntentIsLiteral(
        string language, string extension, string? intent)
    {
        string archive = Path.Combine(_root, "authored-node.nupkg");
        File.Copy(FunctionArchive, archive);
        using var environment = new EngineEnvironmentSettings(new NodeAuthoredLayoutHost(intent), settingsLocation: Path.Combine(_root, "authored-hive"));
        using var packages = new TemplatePackageManager(environment);
        var installed = await packages.GetBuiltInManagedProvider(InstallationScope.Global).InstallAsync([new InstallRequest(archive)], CancellationToken.None);
        installed.Should().ContainSingle().Which.Success.Should().BeTrue();
        var template = (await packages.GetTemplatesAsync(CancellationToken.None)).Single(item => item.Identity == "Azure.Functions.Node.Http." + language);
        var creator = new TemplateCreator(environment);
        foreach (string relative in new[] { "project-root", Path.Combine("project-nested", "src", "functions") })
        {
            string target = Directory.CreateDirectory(Path.Combine(_root, relative)).FullName;
            string user = Path.Combine(target, "existing-user.txt");
            File.WriteAllText(user, "preserve");
            var parameters = new Dictionary<string, string?> { ["AuthLevel"] = "function" };

            var preview = await creator.InstantiateAsync(template, "AuthoredHttp", "AuthoredHttp", target, parameters,
                dryRun: true, cancellationToken: CancellationToken.None);
            Directory.GetFiles(target, "*", SearchOption.AllDirectories).Should().Equal(user);
            var created = await creator.InstantiateAsync(template, "AuthoredHttp", "AuthoredHttp", target, parameters,
                cancellationToken: CancellationToken.None);

            preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
            created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
            string output = intent == "legacy"
                ? Path.Combine(target, "src", "functions", "AuthoredHttp" + extension) : Path.Combine(target, "AuthoredHttp" + extension);
            File.ReadAllText(output).Should().Contain("app.http('AuthoredHttp'", "authLevel: 'function'");
            File.ReadAllText(user).Should().Be("preserve");
            Directory.GetFiles(target, "*", SearchOption.AllDirectories).Should().HaveCount(2);
            created.CreationEffects!.FileChanges.Select(change => change.TargetRelativePath).Should()
                .BeEquivalentTo(preview.CreationEffects!.FileChanges.Select(change => change.TargetRelativePath));
            preview.CreationEffects!.FileChanges.Should().ContainSingle().Which.TargetRelativePath
                .Should().Be(intent == "legacy" ? "src/functions/AuthoredHttp" + extension : "./AuthoredHttp" + extension);
        }
        template.ParameterDefinitions.Should().NotContain(parameter => parameter.Name == "InvocationOutputIntent");
    }

    [Fact]
    public async Task AuthoredLayout_SameInstalledHive_FreshIntentSessionsDoNotReusePreviousBranchOrUserOverride()
    {
        string archive = Path.Combine(_root, "authored-cache.nupkg");
        File.Copy(FunctionArchive, archive);
        string hive = Path.Combine(_root, "same-authored-hive");
        string?[] modes = ["literal", "legacy", null, "legacy", "literal"];
        for (int index = 0; index < modes.Length; index++)
        {
            using var environment = new EngineEnvironmentSettings(new NodeAuthoredLayoutHost(modes[index]), settingsLocation: hive);
            using var packages = new TemplatePackageManager(environment);
            if (index == 0)
            {
                (await packages.GetBuiltInManagedProvider(InstallationScope.Global).InstallAsync([new InstallRequest(archive)], CancellationToken.None))
                    .Should().ContainSingle().Which.Success.Should().BeTrue();
            }
            foreach (var template in (await packages.GetTemplatesAsync(CancellationToken.None)).Where(template => template.ShortNameList.Contains("http")))
            {
                string extension = template.Identity.EndsWith("JavaScript", StringComparison.Ordinal) ? ".js" : ".ts";
                string target = Path.Combine(_root, "cached-output-" + index + extension);
                var creator = new TemplateCreator(environment);
                var parameters = new Dictionary<string, string?> { ["InvocationOutputIntent"] = modes[index] == "legacy" ? "literal" : "legacy" };

                var preview = await creator.InstantiateAsync(template, "CachedHttp", "CachedHttp", target, parameters,
                    dryRun: true, cancellationToken: CancellationToken.None);
                var created = await creator.InstantiateAsync(template, "CachedHttp", "CachedHttp", target, parameters,
                    cancellationToken: CancellationToken.None);

                preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
                created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
                string expected = modes[index] == "legacy" ? Path.Combine(target, "src", "functions", "CachedHttp" + extension)
                    : Path.Combine(target, "CachedHttp" + extension);
                File.Exists(expected).Should().BeTrue();
                Directory.GetFiles(target, "*", SearchOption.AllDirectories).Should().ContainSingle();
                created.CreationEffects!.FileChanges.Select(change => change.TargetRelativePath).Should()
                    .BeEquivalentTo(preview.CreationEffects!.FileChanges.Select(change => change.TargetRelativePath));
            }
        }
    }

    private sealed class NodeAuthoredLayoutHost(string? intent) : DefaultTemplateEngineHost("func", "0.0.0-authored-layout",
        intent is null ? [] : new Dictionary<string, string> { ["func:item-output-intent"] = intent },
        [.. Components.AllComponents, .. RunnableComponents.AllComponents]);
}
