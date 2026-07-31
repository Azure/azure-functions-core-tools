// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Integration coverage for <see cref="FuncTemplateScaffolder"/> over the real
/// Node/Python template packages: the create-flow (dry-run conflict detection,
/// token replacement, source rename), constraint gating, engine status mapping,
/// and the Python v2 append flows (append-to-existing and blueprint-create).
/// </summary>
public sealed class FuncTemplateScaffolderTests
{
    private static ParseResult EmptyParse() => new RootCommand().Parse(Array.Empty<string>());

    private static NewContext CreateContext(
        string projectDirectory,
        string templateId,
        string stack,
        string language,
        string functionName,
        bool force = false,
        IReadOnlyDictionary<string, string?>? userOptionValues = null)
    {
        var template = new FunctionTemplateInfo(
            templateId, stack, templateId, null, null, [language], new TemplateMetadata([], false, null));
        var workingDirectory = new WorkingDirectory(new DirectoryInfo(projectDirectory), true, projectDirectory);
        return new NewContext(workingDirectory, template, functionName, language, force, UserOptionValues: userOptionValues);
    }

    [Fact]
    public async Task ApplyAsync_NodeTypeScriptCreate_RenamesSourceAndReplacesTokens()
    {
        using var harness = new EngineIntegrationHarness();
        harness.UseExtensionBundle("4.5.0");
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateScaffolder scaffolder = harness.CreateScaffolder();
        string projectDirectory = harness.NewProjectDirectory();

        NewContext context = CreateContext(
            projectDirectory, "http", "node", "typescript", "MyHttp",
            userOptionValues: new Dictionary<string, string?> { ["AuthLevel"] = "function" });

        TemplateApplicationResult result = await scaffolder.ApplyAsync(context, EmptyParse(), CancellationToken.None);

        result.Should().BeOfType<TemplateApplicationResult.Created>();
        var created = (TemplateApplicationResult.Created)result;
        created.Files.Select(f => f.Replace('\\', '/')).Should().Contain(f => f.EndsWith("src/functions/MyHttp.ts"));

        string outputFile = Path.Combine(projectDirectory, "src", "functions", "MyHttp.ts");
        File.Exists(outputFile).Should().BeTrue();
        File.Exists(Path.Combine(projectDirectory, "src", "functions", "HttpTriggerFunc.ts")).Should().BeFalse();

        string content = await File.ReadAllTextAsync(outputFile, CancellationToken.None);
        content.Should().Contain("export async function MyHttp");
        content.Should().Contain("app.http('MyHttp'");
        content.Should().Contain("authLevel: 'function'");
        content.Should().NotContain("AUTH_LEVEL_VALUE");
    }

    [Fact]
    public async Task ApplyAsync_ExistingOutputWithoutForce_ReturnsAlreadyExistsAndWritesNothing()
    {
        using var harness = new EngineIntegrationHarness();
        harness.UseExtensionBundle("4.5.0");
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateScaffolder scaffolder = harness.CreateScaffolder();
        string projectDirectory = harness.NewProjectDirectory();

        await scaffolder.ApplyAsync(
            CreateContext(projectDirectory, "http", "node", "typescript", "MyHttp"), EmptyParse(), CancellationToken.None);

        string outputFile = Path.Combine(projectDirectory, "src", "functions", "MyHttp.ts");
        string before = await File.ReadAllTextAsync(outputFile, CancellationToken.None);

        TemplateApplicationResult result = await scaffolder.ApplyAsync(
            CreateContext(projectDirectory, "http", "node", "typescript", "MyHttp"), EmptyParse(), CancellationToken.None);

        result.Should().BeOfType<TemplateApplicationResult.AlreadyExists>();
        ((TemplateApplicationResult.AlreadyExists)result).ExistingFiles.Should().NotBeEmpty();
        (await File.ReadAllTextAsync(outputFile, CancellationToken.None)).Should().Be(before);
    }

    [Fact]
    public async Task ApplyAsync_ExistingOutputWithForce_Overwrites()
    {
        using var harness = new EngineIntegrationHarness();
        harness.UseExtensionBundle("4.5.0");
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateScaffolder scaffolder = harness.CreateScaffolder();
        string projectDirectory = harness.NewProjectDirectory();

        await scaffolder.ApplyAsync(
            CreateContext(projectDirectory, "http", "node", "typescript", "MyHttp"), EmptyParse(), CancellationToken.None);

        TemplateApplicationResult result = await scaffolder.ApplyAsync(
            CreateContext(projectDirectory, "http", "node", "typescript", "MyHttp", force: true), EmptyParse(), CancellationToken.None);

        result.Should().BeOfType<TemplateApplicationResult.Created>();
    }

    [Fact]
    public async Task ApplyAsync_UnknownTemplate_ReturnsProviderError()
    {
        using var harness = new EngineIntegrationHarness();
        harness.UseExtensionBundle("4.5.0");
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateScaffolder scaffolder = harness.CreateScaffolder();
        string projectDirectory = harness.NewProjectDirectory();

        TemplateApplicationResult result = await scaffolder.ApplyAsync(
            CreateContext(projectDirectory, "does-not-exist", "node", "typescript", "MyHttp"), EmptyParse(), CancellationToken.None);

        result.Should().BeOfType<TemplateApplicationResult.Failed>();
        ((TemplateApplicationResult.Failed)result).Failure.Should().BeOfType<TemplateApplicationFailure.ProviderError>();
    }

    [Fact]
    public async Task ApplyAsync_RestrictedTemplateWithoutBundle_ReturnsMissingExtensionBundle()
    {
        using var harness = new EngineIntegrationHarness();
        await harness.InstallAsync(EngineIntegrationHarness.NodePackageId, CancellationToken.None);
        FuncTemplateScaffolder scaffolder = harness.CreateScaffolder();
        string projectDirectory = harness.NewProjectDirectory();

        TemplateApplicationResult result = await scaffolder.ApplyAsync(
            CreateContext(projectDirectory, "http", "node", "typescript", "MyHttp"), EmptyParse(), CancellationToken.None);

        result.Should().BeOfType<TemplateApplicationResult.Failed>();
        var failure = ((TemplateApplicationResult.Failed)result).Failure;
        failure.Should().BeOfType<TemplateApplicationFailure.MissingExtensionBundle>();
        ((TemplateApplicationFailure.MissingExtensionBundle)failure).SuggestedBundleId
            .Should().Be(EngineIntegrationHarness.ExtensionBundleId);
    }

    [Fact]
    public async Task ApplyAsync_PythonAppendToExistingApp_AppendsBoundToApp()
    {
        using var harness = new EngineIntegrationHarness();
        await harness.InstallAsync(EngineIntegrationHarness.PythonPackageId, CancellationToken.None);
        FuncTemplateScaffolder scaffolder = harness.CreateScaffolder();
        string projectDirectory = harness.NewProjectDirectory();

        string appFile = Path.Combine(projectDirectory, "function_app.py");
        await File.WriteAllTextAsync(
            appFile,
            "import azure.functions as func\n\napp = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)\n",
            CancellationToken.None);

        TemplateApplicationResult result = await scaffolder.ApplyAsync(
            CreateContext(projectDirectory, "http", "python", "python", "MyPyHttp"), EmptyParse(), CancellationToken.None);

        result.Should().BeOfType<TemplateApplicationResult.Created>();
        var created = (TemplateApplicationResult.Created)result;
        created.Files.Should().BeEmpty();
        created.Modified.Should().Contain("function_app.py");

        string content = await File.ReadAllTextAsync(appFile, CancellationToken.None);
        content.Should().Contain("app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)");
        content.Should().Contain("@app.route(");
        content.Should().Contain("def MyPyHttp(");
    }

    [Fact]
    public async Task ApplyAsync_PythonAppendToMissingBlueprintFile_CreatesBlueprintAndPrintsRegistration()
    {
        using var harness = new EngineIntegrationHarness();
        await harness.InstallAsync(EngineIntegrationHarness.PythonPackageId, CancellationToken.None);
        FuncTemplateScaffolder scaffolder = harness.CreateScaffolder();
        string projectDirectory = harness.NewProjectDirectory();

        NewContext context = CreateContext(
            projectDirectory, "http", "python", "python", "MyPyHttp",
            userOptionValues: new Dictionary<string, string?> { ["AppFile"] = "api.py" });

        TemplateApplicationResult result = await scaffolder.ApplyAsync(context, EmptyParse(), CancellationToken.None);

        result.Should().BeOfType<TemplateApplicationResult.Created>();
        var created = (TemplateApplicationResult.Created)result;
        created.Modified.Should().Contain("api.py");
        created.Messages.Should().Contain(m => m.Contains("from api import bp"));
        created.Messages.Should().Contain(m => m.Contains("app.register_functions(bp)"));

        string blueprintFile = Path.Combine(projectDirectory, "api.py");
        File.Exists(blueprintFile).Should().BeTrue();
        File.Exists(Path.Combine(projectDirectory, "function_app.py")).Should().BeFalse();

        string content = await File.ReadAllTextAsync(blueprintFile, CancellationToken.None);
        content.Should().Contain("bp = func.Blueprint()");
        content.Should().Contain("@bp.route(");
        content.Should().Contain("def MyPyHttp(");
    }

    [Fact]
    public async Task ApplyAsync_PythonAppendWriteFails_PreservesStagedSnippetAndOrphansNothing()
    {
        using var harness = new EngineIntegrationHarness();
        await harness.InstallAsync(EngineIntegrationHarness.PythonPackageId, CancellationToken.None);
        FuncTemplateScaffolder scaffolder = harness.CreateScaffolder(
            postActionFileSystem: new WriteFailingFileSystem(),
            stagingArea: new RootedStagingArea(Path.Combine(harness.Root, "staging")));
        string projectDirectory = harness.NewProjectDirectory();

        TemplateApplicationResult result = await scaffolder.ApplyAsync(
            CreateContext(projectDirectory, "http", "python", "python", "MyPyHttp"), EmptyParse(), CancellationToken.None);

        var failed = result.Should().BeOfType<TemplateApplicationResult.Failed>().Subject;
        var providerError = failed.Failure.Should().BeOfType<TemplateApplicationFailure.ProviderError>().Subject;
        string stagedPath = ExtractRecoveryPath(providerError.Message);

        // The recovery path the error advertises must actually survive on disk...
        File.Exists(stagedPath).Should().BeTrue("the error promises the staged snippet is preserved for manual recovery");

        // ...while nothing is orphaned in the project: the failed write left no .py behind.
        Directory.EnumerateFiles(projectDirectory, "*.py", SearchOption.AllDirectories)
            .Should().BeEmpty("a failed append must not write into the project tree");
    }

    private static string ExtractRecoveryPath(string message)
    {
        const string prefix = "preserved at '";
        int start = message.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        int end = message.IndexOf('\'', start);
        return message[start..end];
    }

    private sealed class WriteFailingFileSystem : IFuncTemplateFileSystem
    {
        private readonly PhysicalFuncTemplateFileSystem _inner = new();

        public bool FileExists(string path) => _inner.FileExists(path);

        public string ReadAllText(string path) => _inner.ReadAllText(path);

        public void WriteAllText(string path, string content) => throw new IOException("Simulated write failure.");

        public void AppendAllText(string path, string content) => throw new IOException("Simulated write failure.");

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern) =>
            _inner.EnumerateFiles(directory, searchPattern);
    }

    private sealed class RootedStagingArea(string root) : IFuncTemplateStagingArea
    {
        public string Create()
        {
            string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public void Cleanup(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
