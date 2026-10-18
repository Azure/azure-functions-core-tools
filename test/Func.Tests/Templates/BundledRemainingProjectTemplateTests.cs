// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-native-project-parity-").FullName;
    private readonly PhysicalFileSystem _files = new();
    private readonly List<WorkloadLoadContext> _contexts = [];

    [Theory]
    [InlineData("Go", "go")]
    [InlineData("Java", "java")]
    [InlineData("PowerShell", "powershell")]
    public async Task Pack_ActualStack_DeclaresExactProjectOnlyArchiveAndPreservesOwnedBytes(string stack, string alias)
    {
        using var outer = new PackageArchiveReader(BuiltArchive(stack));
        outer.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Equal("FuncCliWorkload");
        using var manifest = JsonDocument.Parse(Read(outer, "bundled-templates.json"));
        manifest.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        var asset = manifest.RootElement.GetProperty("assets").EnumerateArray().Should().ContainSingle().Subject;
        string path = $"tools/any/templates/{alias}-basic.nupkg";
        asset.GetProperty("path").GetString().Should().Be(path);
        using var bytes = new MemoryStream();
        outer.GetStream(path).CopyTo(bytes);
        asset.GetProperty("sha256").GetString().Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        bytes.Position = 0;
        using var inner = new PackageArchiveReader(bytes);
        string id = $"Azure.Functions.Templates.{stack}.Basic";
        inner.NuspecReader.GetId().Should().Be(id);
        inner.NuspecReader.GetVersion().Should().Be(outer.NuspecReader.GetVersion());
        inner.NuspecReader.GetPackageTypes().Select(type => type.Name).Should().Equal("FuncTemplate");
        asset.GetProperty("packageId").GetString().Should().Be(id);
        asset.GetProperty("version").GetString().Should().Be(inner.NuspecReader.GetVersion().ToNormalizedString());
        var inventory = asset.GetProperty("templates").EnumerateArray().Should().ContainSingle().Subject;
        inventory.GetProperty("path").GetString().Should().Be("content/project/.template.config/template.json");
        inventory.GetProperty("identity").GetString().Should().Be($"Azure.Functions.{stack}.Basic");
        inventory.GetProperty("type").GetString().Should().Be("project");
        inner.GetFiles().Where(file => file.EndsWith("/.template.config/template.json", StringComparison.Ordinal))
            .Should().Equal("content/project/.template.config/template.json");
        inner.GetFiles().Should().Contain("LICENSE", "README.md", "content/project/.gitignore");
        Read(inner, "LICENSE").Should().Contain("MIT", ".NET Foundation");
        var template = JsonNode.Parse(Read(inner, "content/project/.template.config/template.json"))!;
        template["constraints"]!["stack"]!["type"]!.GetValue<string>().Should().Be("func-workload");
        template["constraints"]!["stack"]!["args"]!.GetValue<string>().Should().Be(alias);
        template["constraints"]!.AsObject().Should().NotContain(pair =>
            pair.Value!["type"]!.GetValue<string>() == "func-bundle");

        using var installed = await InstallAsync(stack);
        var initializer = installed.GetRequiredService<IProjectInitializer>();
        var metadata = installed.GetRequiredService<IProjectStack>();
        initializer.Stack.Should().Be(metadata.Stack).And.Be(alias);
        initializer.SupportedLanguages.Should().Equal(metadata.SupportedLanguages);
        initializer.WorkerRuntimeAliases.Should().Equal(metadata.WorkerRuntimeAliases);
        var catalog = new InstalledProjectStackCatalog([metadata]);
        foreach (string languageAlias in metadata.SupportedLanguageAliases.SelectMany(pair => pair.Value))
        {
            catalog.GetCanonicalLanguage(languageAlias).Should().Be(stack);
        }

        var sourceFiles = stack switch
        {
            "Go" => new[] { ("go.mod", "go.mod"), ("main.go", "main.go"), ("funcignore", ".funcignore") },
            "Java" => [("pom.xml", "pom.xml"), ("Function.java", "src/main/java/com/function/Function.java"), ("funcignore", ".funcignore")],
            _ => [("profile.ps1", "profile.ps1"), ("requirements.psd1", ".template.config/requirements.psd1")],
        };
        Assembly assembly = initializer.GetType().Assembly;
        foreach (var (resource, destination) in sourceFiles.Append(("gitignore", ".gitignore")).Append(("local.settings.json", "local.settings.json")))
        {
            string resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(".Templates." + resource, StringComparison.Ordinal));
            using var original = assembly.GetManifestResourceStream(resourceName)!;
            using var packaged = inner.GetStream("content/project/" + destination);
            SHA256.HashData(packaged).Should().Equal(SHA256.HashData(original));
        }
    }

    [Theory]
    [InlineData("Go", "go", "Go", "Stable", false)]
    [InlineData("Go", "go", "Go", "Preview", false)]
    [InlineData("Go", "go", "Go", "Experimental", false)]
    [InlineData("Go", "go", "Go", "Stable", true)]
    [InlineData("Java", "java", "Java", "Stable", false)]
    [InlineData("Java", "java", "Java", "Preview", false)]
    [InlineData("Java", "java", "Java", "Experimental", false)]
    [InlineData("Java", "java", "Java", "Stable", true)]
    [InlineData("PowerShell", "powershell", "PowerShell", "Stable", false)]
    [InlineData("PowerShell", "powershell", "PowerShell", "Preview", false)]
    [InlineData("PowerShell", "powershell", "PowerShell", "Experimental", false)]
    [InlineData("PowerShell", "powershell", "PowerShell", "Stable", true)]
    public async Task Project_InstalledOwnedMetadata_PreviewCreateAndResolvePreserveScaffold(
        string stack, string alias, string language, string channel, bool noBundles)
    {
        using var services = await InstallAsync(stack);
        string output = Path.Combine(_root, "created");
        var directory = WorkingDirectory.FromExplicit(output);
        using var templater = ProjectTemplater(services, directory, alias);
        var resolution = await templater.ResolveGroupAsync($"Azure.Functions.{stack}.Basic", TemplateType.Project, CancellationToken.None);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success, Diagnostics(resolution));
        var selected = resolution.Group!.Should().ContainSingle().Subject;
        selected.Entry.Parameters.Should().Contain(parameter =>
            parameter.CanonicalName == "BundlesChannel" && parameter.LongName == "bundles-channel" && parameter.ShortName == "c");
        selected.Entry.Parameters.Should().Contain(parameter =>
            parameter.CanonicalName == "NoBundles" && parameter.LongName == "no-bundles" && parameter.DefaultValue == "false");
        Dictionary<string, string?> parameters = new() { ["BundlesChannel"] = channel, ["NoBundles"] = noBundles.ToString() };
        if (stack == "Go")
        {
            selected.Entry.Parameters.Should().Contain(parameter =>
                parameter.CanonicalName == "SkipGoModTidy" && parameter.LongName == "skip-go-mod-tidy" && parameter.DefaultValue == "false");
            parameters["SkipGoModTidy"] = "true";
        }
        var request = new TemplateInvocationRequest(directory, parameters, " My App_42.! ", DryRun: true);

        var preview = await selected.InvokeAsync(request, CancellationToken.None);

        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        Directory.Exists(output).Should().BeFalse();
        preview.Changes.Should().Contain(change => change.Path.Replace('\\', '/') == ".func/config.json");

        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.ConfiguredProjects.Should().Equal(output);
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(output, ".func", "config.json")))!;
        config["stack"]!["runtime"]!.GetValue<string>().Should().Be(alias);
        config["stack"]!["language"]!.GetValue<string>().Should().Be(language);
        AssertHost(output, channel, noBundles);
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "local.settings.json")))!;
        settings["Values"]!["FUNCTIONS_WORKER_RUNTIME"]!.GetValue<string>().Should().Be(stack == "Go" ? "native" : alias);
        settings["Values"]!["AzureWebJobsStorage"]!.GetValue<string>().Should().Be("UseDevelopmentStorage=true");
        File.Exists(Path.Combine(output, ".funcignore")).Should().Be(stack != "PowerShell");
        File.Exists(Path.Combine(output, ".gitignore")).Should().BeTrue();
        if (stack == "Go")
        {
            File.ReadAllText(Path.Combine(output, "go.mod")).Should().StartWith("module my-app_42").And.Contain("go 1.24").And.NotContain("require");
            File.ReadAllText(Path.Combine(output, "main.go")).Should().Contain("sdk.FunctionApp()", "worker.Start(app)");
        }
        else if (stack == "Java")
        {
            File.ReadAllText(Path.Combine(output, "pom.xml")).Should().Contain("<artifactId>my-app_42</artifactId>",
                "<functionAppName>my-app_42</functionAppName>", "<maven.compiler.release>21</maven.compiler.release>");
            File.ReadAllText(Path.Combine(output, "src", "main", "java", "com", "function", "Function.java"))
                .Should().Contain("@FunctionName(\"HttpExample\")", "AuthorizationLevel.ANONYMOUS");
        }
        else
        {
            settings["Values"]!["FUNCTIONS_WORKER_RUNTIME_VERSION"]!.GetValue<string>().Should().Be("7.4");
            File.ReadAllText(Path.Combine(output, "profile.ps1")).Should().Contain("Connect-AzAccount -Identity");
            File.Exists(Path.Combine(output, "requirements.psd1")).Should().BeFalse();
        }
        var recognized = await services.GetRequiredService<IFunctionsProjectResolver>()
            .ResolveProjectAsync(new(directory), CancellationToken.None);
        var project = recognized.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project;
        project.StackName.Should().Be(alias);
        project.Language.Should().BeNull("single-language project factories leave the language implied by the stack");
    }

    [Theory]
    [InlineData("Go", "go", "!!!", "app")]
    [InlineData("Java", "java", "!!!", "azure-functions-java")]
    [InlineData("Go", "go", " M\u00c9 App_42! ", "m-app_42")]
    [InlineData("Java", "java", " M\u00c9 App_42! ", "m-app_42")]
    [InlineData("Go", "go", null, "default-name")]
    [InlineData("Java", "java", null, "default-name")]
    public async Task Project_NameForms_MatchInitializerNormalizationAndDirectoryFallback(string stack, string alias, string? name, string expected)
    {
        using var services = await InstallAsync(stack);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "Default Name"));
        using var templater = ProjectTemplater(services, output, alias);
        var selected = (await templater.ResolveGroupAsync($"Azure.Functions.{stack}.Basic", TemplateType.Project, CancellationToken.None)).Group!.Single();

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), name), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        string manifest = File.ReadAllText(Path.Combine(output.Info.FullName, stack == "Go" ? "go.mod" : "pom.xml"));
        manifest.Should().Contain(stack == "Go" ? "module " + expected : "<artifactId>" + expected + "</artifactId>");
    }

    [Theory]
    [InlineData("Go", "go")]
    [InlineData("Java", "java")]
    [InlineData("PowerShell", "powershell")]
    public async Task Project_AbsentWorkloadOrInvalidParameters_RefusesWithoutCreatingFiles(string stack, string alias)
    {
        using var services = await InstallAsync(stack);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "refused"));
        using (var unavailable = ProjectTemplater(services, output, alias, installed: false))
        {
            var result = await unavailable.ResolveGroupAsync($"Azure.Functions.{stack}.Basic", TemplateType.Project, CancellationToken.None);
            result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
            Directory.Exists(output.Info.FullName).Should().BeFalse();
        }
        using var templater = ProjectTemplater(services, output, alias);
        var selected = (await templater.ResolveGroupAsync($"Azure.Functions.{stack}.Basic", TemplateType.Project, CancellationToken.None)).Group!.Single();

        var invalid = await selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["BundlesChannel"] = "Unsupported" }), CancellationToken.None);

        invalid.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData("Go", "go", "main.go")]
    [InlineData("Java", "java", "pom.xml")]
    [InlineData("PowerShell", "powershell", "profile.ps1")]
    public async Task Project_ExistingSource_RefusesOverwriteAndPreservesBytes(string stack, string alias, string file)
    {
        using var services = await InstallAsync(stack);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "existing"));
        Directory.CreateDirectory(output.Info.FullName);
        File.WriteAllText(Path.Combine(output.Info.FullName, file), "user-owned bytes");
        using var templater = ProjectTemplater(services, output, alias);
        var selected = (await templater.ResolveGroupAsync($"Azure.Functions.{stack}.Basic", TemplateType.Project, CancellationToken.None)).Group!.Single();

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>()), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        File.ReadAllText(Path.Combine(output.Info.FullName, file)).Should().Be("user-owned bytes");
        Directory.GetFiles(output.Info.FullName).Should().ContainSingle();
        Directory.Exists(Path.Combine(output.Info.FullName, ".func")).Should().BeFalse();
    }

    [Theory]
    [InlineData("7.4", false, "Stable", false)]
    [InlineData("7.6", false, "Preview", false)]
    [InlineData("7.4", false, "Experimental", false)]
    [InlineData("7.6", true, "Stable", false)]
    [InlineData("7.4", true, "Preview", true)]
    [InlineData("7.6", false, "Experimental", true)]
    public async Task PowerShell_BaseArchive_PreservesRuntimeAndBundleOptionsAndReservesConditionalRequirements(
        string runtime, bool managed, string channel, bool noBundles)
    {
        using var services = await InstallAsync("PowerShell");
        string output = Path.Combine(_root, "powershell-created");
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(output)), Hive, Snapshot("powershell"));
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        TemplateCreator creator = new(session.Settings);
        Dictionary<string, string?> parameters = new()
        {
            ["RuntimeVersion"] = runtime,
            ["ManagedDependencies"] = managed.ToString(),
            ["NoBundles"] = noBundles.ToString(),
            ["BundlesChannel"] = channel,
        };

        var preview = await creator.InstantiateAsync(template, "PowerShellApp", "PowerShellApp", output, parameters,
            dryRun: true, cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success, preview.ErrorMessage);
        Directory.Exists(output).Should().BeFalse();

        var created = await creator.InstantiateAsync(template, "PowerShellApp", "PowerShellApp", output, parameters,
            cancellationToken: CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        AssertHost(output, channel, noBundles);
        var host = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "host.json")))!;
        (host["managedDependency"] is not null).Should().Be(managed);
        if (managed) host["managedDependency"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
        File.Exists(Path.Combine(output, "requirements.psd1")).Should().BeFalse("only the trusted preparation effect may create requirements");
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "local.settings.json")))!;
        settings["Values"]!["FUNCTIONS_WORKER_RUNTIME_VERSION"]!.GetValue<string>().Should().Be(runtime);
        settings["Values"]!["FUNCTIONS_WORKER_RUNTIME"]!.GetValue<string>().Should().Be("powershell");
        File.ReadAllText(Path.Combine(output, "profile.ps1")).Should().Contain("Connect-AzAccount -Identity");
        File.Exists(Path.Combine(output, ".gitignore")).Should().BeTrue();
        File.Exists(Path.Combine(output, ".funcignore")).Should().BeFalse();
        var recognized = await services.GetRequiredService<IFunctionsProjectResolver>()
            .ResolveProjectAsync(new(WorkingDirectory.FromExplicit(output)), CancellationToken.None);
        recognized.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project.StackName.Should().Be("powershell");
    }

    [Fact]
    public async Task PowerShell_DefaultsAndUnsupportedRuntime_PreserveInitializerContract()
    {
        using var services = await InstallAsync("PowerShell");
        string output = Path.Combine(_root, "powershell-defaults");
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(output)), Hive, Snapshot("powershell"));
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        TemplateCreator creator = new(session.Settings);
        string invalidOutput = Path.Combine(_root, "invalid-runtime");

        var invalidDirectory = WorkingDirectory.FromExplicit(invalidOutput);
        using var templater = ProjectTemplater(services, invalidDirectory, "powershell");
        var selected = (await templater.ResolveGroupAsync("Azure.Functions.PowerShell.Basic", TemplateType.Project, CancellationToken.None)).Group!.Single();

        var invalid = await selected.InvokeAsync(
            new(invalidDirectory, new Dictionary<string, string?> { ["RuntimeVersion"] = "6.0" }), CancellationToken.None);

        invalid.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        Directory.Exists(invalidOutput).Should().BeFalse();

        var created = await creator.InstantiateAsync(template, "Default", "Default", output,
            new Dictionary<string, string?>(), cancellationToken: CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
        File.ReadAllText(Path.Combine(output, "local.settings.json")).Should().Contain("\"7.4\"");
        File.Exists(Path.Combine(output, "requirements.psd1")).Should().BeFalse();
        JsonNode.Parse(File.ReadAllText(Path.Combine(output, "host.json")))!.AsObject().Should().NotContainKey("managedDependency");
    }

    [Fact]
    public async Task PowerShell_BaseProject_UsesQualifiedPlannerWithoutConditionalSourceOrFilenameRules()
    {
        using var services = await InstallAsync("PowerShell");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "planner-preview"));
        using var templater = ProjectTemplater(services, output, "powershell");

        var result = await templater.ResolveGroupAsync("Azure.Functions.PowerShell.Basic", TemplateType.Project, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Success, Diagnostics(result));
        var preview = await result.Group!.Single().InvokeAsync(
            new(output, new Dictionary<string, string?> { ["ManagedDependencies"] = "true" }, DryRun: true), CancellationToken.None);
        preview.Status.Should().Be(TemplateInvocationStatus.Success, preview.Diagnostic);
        preview.Changes.Should().NotContain(change => change.Path.EndsWith("requirements.psd1", StringComparison.Ordinal));
        Directory.Exists(output.Info.FullName).Should().BeFalse();
        var entry = (await templater.ListAsync(TemplateType.Project, CancellationToken.None)).Single();
        entry.Parameters.Should().Contain(parameter => parameter.CanonicalName == "ManagedDependencies"
            && parameter.LongName == "managed-dependencies" && parameter.DefaultValue == "false");
    }

    [Theory]
    [InlineData(null, "destination", "stack")]
    [InlineData("", "destination", "stack")]
    [InlineData("Unsupported", "destination", "stack")]
    [InlineData("Go", null, "parentDirectory")]
    [InlineData("Go", "", "parentDirectory")]
    public async Task ExportProjectSmokeFixtureAsync_InvalidArguments_RefusesBeforeInstallation(string? stack, string? output, string parameter)
    {
        Func<Task> action = () => ExportProjectSmokeFixtureAsync(stack!, output!, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName(parameter);
    }

    [Fact]
    public async Task ExportProjectSmokeFixtureAsync_NonemptyOutput_PreservesUserContent()
    {
        string output = Path.Combine(_root, "user-output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "user.txt"), "user bytes");

        Func<Task> action = () => ExportProjectSmokeFixtureAsync("Go", output, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("parentDirectory");
        File.ReadAllText(Path.Combine(output, "user.txt")).Should().Be("user bytes");
        Directory.GetFiles(output).Should().ContainSingle();
    }

    [Fact]
    public async Task ExportProjectSmokeFixtureAsync_PreCancelled_DoesNotInstallOrCreateOutput()
    {
        string output = Path.Combine(_root, "cancelled-output");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> action = () => ExportProjectSmokeFixtureAsync("Go", output, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output).Should().BeFalse();
    }

    private string Home => Path.Combine(_root, "home");

    private string Hive => Path.Combine(Home, "templates");

    private static string BuiltArchive(string stack)
        => Directory.GetFiles(Path.Combine(Path.GetDirectoryName(typeof(BundledRemainingProjectTemplateTests).Assembly.Location)!, "bundled-project-artifacts"),
            $"Azure.Functions.Cli.Workloads.{stack}.*.nupkg").Single();

    public static async Task<string> ExportProjectSmokeFixtureAsync(
        string stack, string parentDirectory, CancellationToken cancellationToken, string? archive = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (stack is not ("Go" or "Java" or "PowerShell"))
        {
            throw new ArgumentException("Select Go, Java or PowerShell.", nameof(stack));
        }
        if (Directory.Exists(parentDirectory) && Directory.EnumerateFileSystemEntries(parentDirectory).Any())
        {
            throw new ArgumentException("The smoke fixture requires an empty output directory.", nameof(parentDirectory));
        }
        using BundledRemainingProjectTemplateTests fixture = new();
        using var services = await fixture.InstallAsync(stack, cancellationToken, archive);
        var output = WorkingDirectory.FromExplicit(parentDirectory);
        string alias = stack.ToLowerInvariant();
        using var templater = fixture.ProjectTemplater(services, output, alias);
        var resolution = await templater.ResolveGroupAsync($"Azure.Functions.{stack}.Basic", TemplateType.Project, cancellationToken);
        resolution.Status.Should().Be(TemplateResolutionStatus.Success, Diagnostics(resolution));
        var created = await resolution.Group!.Single().InvokeAsync(
            new(output, new Dictionary<string, string?> { ["NoBundles"] = "true" }, "SmokeApp"), cancellationToken);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        return parentDirectory;
    }

    private async Task<ServiceProvider> InstallAsync(string stack, CancellationToken cancellationToken = default, string? archive = null)
    {
        WorkloadPathsOptions paths = new(Path.Combine(_root, "workloads"));
        FileWorkloadOperationCoordinator coordinator = new(paths);
        WorkloadStore workloads = new(paths, coordinator);
        TemplateStore templates = new(new(Home), _files, new FileTemplateStoreLock(), NullLoggerFactory.Instance);
        WorkloadMetadataReader metadata = new();
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        WorkloadRidPackageSelector selector = new(runtime);
        BundledTemplateInstaller bundled = new(templates, new BasicArtifactInspector(_files), _files, new(Home));
        WorkloadInstaller installer = new(Substitute.For<IWorkloadPackageSource>(), new WorkloadPackageInspector(metadata, selector), selector,
            new WorkloadDeploymentService(paths, workloads, metadata, coordinator, _files, bundled), coordinator, workloads, _files);
        string package = archive ?? BuiltArchive(stack);
        var installed = await installer.InstallFromPackageAsync(package, cancellationToken: cancellationToken);
        string before = File.ReadAllText(paths.WorkloadRegistryPath);
        (await installer.InstallFromPackageAsync(package, cancellationToken: cancellationToken)).AlreadyInstalled.Should().BeTrue();
        File.ReadAllText(paths.WorkloadRegistryPath).Should().Be(before);
        installed.Entry.BundledTemplatesDeclared.Should().BeTrue();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IInteractionService>(new TestInteractionService());
        await WorkloadRegistration.RegisterWorkloadsAsync(services, paths, new TestInteractionService(), cancellationToken);
        _contexts.AddRange(services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>().Select(workload => workload.LoadContext));
        services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        return services.BuildServiceProvider();
    }

    private Templater ProjectTemplater(ServiceProvider services, WorkingDirectory output, string stack, bool installed = true)
    {
        TemplateProjectConfigurationPlanner planner = new(new(services.GetServices<IProjectStack>()));
        TemplateProjectConfigurationWriter writer = new(_files);
        return new(new(new(output), Hive, installed ? Snapshot(stack) : new([]), new(planner, writer)));
    }

    private static TemplateConstraintEvaluationSnapshot Snapshot(string alias)
        => new([new($"Azure.Functions.Cli.Workloads.{alias}", NuGetVersion.Parse("1.0.0"), [alias], true)]);

    private static string Diagnostics(TemplateResolutionResult result)
        => string.Join("; ", result.Diagnostics.Select(entry => entry.ProjectConfigurationDiagnostic ?? entry.MetadataDiagnostic
            ?? entry.ConstraintEvaluation.Status.ToString()));

    private static void AssertHost(string output, string channel, bool noBundles)
    {
        var host = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "host.json")))!;
        host["version"]!.GetValue<string>().Should().Be("2.0");
        (host["extensionBundle"] is null).Should().Be(noBundles);
        if (!noBundles)
        {
            host["extensionBundle"]!["id"]!.GetValue<string>().Should().Be("Microsoft.Azure.Functions.ExtensionBundle"
                + (channel == "Stable" ? string.Empty : "." + channel));
            host["extensionBundle"]!["version"]!.GetValue<string>().Should().Be("[4.*, 5.0.0)");
        }
    }

    private static string Read(PackageArchiveReader archive, string path)
    {
        using var reader = new StreamReader(archive.GetStream(path));
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        foreach (var context in _contexts) context.Unload();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (UnauthorizedAccessException) when (_contexts.Count > 0)
        {
            // Windows can retain the loaded workload DLLs until the test process exits.
        }
    }
}
