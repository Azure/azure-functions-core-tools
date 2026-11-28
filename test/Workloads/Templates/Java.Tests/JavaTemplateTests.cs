// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Azure.Functions.Cli.TestHelpers.Templates;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Workloads.Templates.Java.Tests;

public sealed class JavaTemplateTests : IAsyncLifetime
{
    private const string StableBundleId = "Microsoft.Azure.Functions.ExtensionBundle";
    private static readonly XNamespace _maven = "http://maven.apache.org/POM/4.0.0";

    private readonly TemplateTestHost _host = new(Path.Combine(AppContext.BaseDirectory, "content", "templates"));

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("func-java", "project", "Azure.Functions.Cli.Java.Project")]
    [InlineData("httptrigger", "item", "Azure.Functions.Cli.Java.HttpTrigger")]
    [InlineData("timertrigger", "item", "Azure.Functions.Cli.Java.TimerTrigger")]
    public async Task Templates_AreDiscoveredFromWorkload(string shortName, string type, string identity)
    {
        ITemplateInfo template = await GetTemplateAsync(shortName);

        template.Identity.Should().Be(identity);
        template.TagsCollection["type"].Should().Be(type);
        template.TagsCollection["language"].Should().Be("Java");
        template.TagsCollection["stack"].Should().Be("java");
    }

    [Theory]
    [InlineData("project", "PackageName", "package-name", null)]
    [InlineData("project", "NoHttp", "no-http", null)]
    [InlineData("project", "NoBundle", "no-bundles", null)]
    [InlineData("project", "BundlesChannel", "bundles-channel", "c")]
    [InlineData("httptrigger", "PackageName", "package-name", null)]
    [InlineData("httptrigger", "AuthLevel", "auth-level", "a")]
    [InlineData("timertrigger", "PackageName", "package-name", null)]
    public void TemplateHostMetadata_ExposesOptionAliases(string template, string symbol, string alias, string? shortAlias)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "templates", template, ".template.config", "func.host.json");
        using var metadata = JsonDocument.Parse(File.ReadAllText(path));

        JsonElement symbolInfo = metadata.RootElement.GetProperty("symbolInfo").GetProperty(symbol);
        symbolInfo.GetProperty("longName").GetString().Should().Be(alias);
        if (shortAlias is not null)
        {
            symbolInfo.GetProperty("shortName").GetString().Should().Be(shortAlias);
        }
    }

    [Theory]
    [InlineData(false, false, null, "com.function")]
    [InlineData(true, false, "com.example.orders", "com.example.orders")]
    [InlineData(false, true, "com.example.orders", "com.example.orders")]
    public async Task Project_Options_GenerateUsableProject(bool noHttp, bool noBundle, string? packageName, string expectedPackage)
    {
        ITemplateInfo template = await GetTemplateAsync("func-java");
        Dictionary<string, string?> parameters = [];
        if (noHttp)
        {
            parameters["NoHttp"] = "true";
        }

        if (noBundle)
        {
            parameters["NoBundle"] = "true";
        }

        if (packageName is not null)
        {
            parameters["PackageName"] = packageName;
        }

        ITemplateCreationResult result = await GenerateAsync(template, "My Sample.App", parameters);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        var pom = XDocument.Load(Path.Combine(output, "pom.xml"));
        XElement root = pom.Root!;
        root.Element(_maven + "groupId")!.Value.Should().Be(expectedPackage);
        root.Element(_maven + "artifactId")!.Value.Should().Be("my-sample-app");
        XElement properties = root.Element(_maven + "properties")!;
        properties.Element(_maven + "maven.compiler.release")!.Value.Should().Be("21");
        properties.Element(_maven + "functionAppName")!.Value.Should().Be("my-sample-app");
        string[] artifacts = [.. root.Descendants(_maven + "artifactId").Select(element => element.Value)];
        artifacts.Should().Contain(["azure-functions-java-library", "azure-functions-maven-plugin", "maven-compiler-plugin"]);
        File.Exists(SourcePath(output, expectedPackage, "FunctionHelloWorldSample.java")).Should().Be(!noHttp);
        if (!noHttp)
        {
            File.ReadAllText(SourcePath(output, expectedPackage, "FunctionHelloWorldSample.java"))
                .Should().ContainAll($"package {expectedPackage};", "@FunctionName(\"HelloWorldSample\")", "AuthorizationLevel.ANONYMOUS");
        }

        File.ReadAllText(Path.Combine(output, ".funcignore")).Should().ContainAll(
            "local.settings.json", "__blobstorage__/", "__queuestorage__/", "__azurite_db*__.json");
        File.Exists(Path.Combine(output, ".gitignore")).Should().BeFalse();
        File.Exists(Path.Combine(output, "funcignore.txt")).Should().BeFalse();
        result.CreationResult!.PrimaryOutputs.Select(path => path.Path).Should().Equal("pom.xml");
        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "host.json")));
        host.RootElement.GetProperty("version").GetString().Should().Be("2.0");
        host.RootElement.TryGetProperty("extensionBundle", out JsonElement bundle).Should().Be(!noBundle);
        if (!noBundle)
        {
            bundle.GetProperty("id").GetString().Should().Be(StableBundleId);
            bundle.GetProperty("version").GetString().Should().Be("[4.*, 5.0.0)");
        }

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "local.settings.json")));
        JsonElement values = settings.RootElement.GetProperty("Values");
        values.GetProperty("AzureWebJobsStorage").GetString().Should().Be("UseDevelopmentStorage=true");
        values.TryGetProperty("FUNCTIONS_WORKER_RUNTIME", out _).Should().BeFalse();
        AssertNoTemplateArtifacts(output);
    }

    [Theory]
    [InlineData("Stable", CreationResultStatus.Success, StableBundleId)]
    [InlineData("Preview", CreationResultStatus.Success, StableBundleId + ".Preview")]
    [InlineData("Experimental", CreationResultStatus.Success, StableBundleId + ".Experimental")]
    [InlineData("Nightly", CreationResultStatus.InvalidParamValues, null)]
    public async Task Project_BundlesChannel_ValidatesAndSubstitutesBundleId(
        string channel, CreationResultStatus expectedStatus, string? expectedId)
    {
        ITemplateInfo template = await GetTemplateAsync("func-java");

        ITemplateCreationResult result = await GenerateAsync(template, "app", new Dictionary<string, string?> { ["BundlesChannel"] = channel });

        result.Status.Should().Be(expectedStatus, result.ErrorMessage);
        if (expectedStatus == CreationResultStatus.Success)
        {
            using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.OutputBaseDirectory!, "host.json")));
            host.RootElement.GetProperty("extensionBundle").GetProperty("id").GetString().Should().Be(expectedId);
        }
    }

    [Theory]
    [InlineData("My Sample.App", "my-sample-app")]
    [InlineData("!!!", "functions-app")]
    [InlineData("A", "a-app")]
    [InlineData("CON", "con-app")]
    [InlineData("Com1", "com1-app")]
    [InlineData("lpt9", "lpt9-app")]
    [InlineData("console", "console")]
    public async Task Project_Name_IsNormalizedForMavenAndFunctionsApp(string name, string expected)
    {
        ITemplateInfo template = await GetTemplateAsync("func-java");

        ITemplateCreationResult result = await GenerateAsync(template, name);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        var pom = XDocument.Load(Path.Combine(result.OutputBaseDirectory!, "pom.xml"));
        pom.Root!.Element(_maven + "artifactId")!.Value.Should().Be(expected);
        pom.Root.Element(_maven + "properties")!.Element(_maven + "functionAppName")!.Value.Should().Be(expected);
    }

    [Fact]
    public async Task Project_LongName_IsLimitedToFunctionsAppNameLength()
    {
        ITemplateInfo template = await GetTemplateAsync("func-java");
        string name = new('a', 70);

        ITemplateCreationResult result = await GenerateAsync(template, name);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        var pom = XDocument.Load(Path.Combine(result.OutputBaseDirectory!, "pom.xml"));
        pom.Root!.Element(_maven + "properties")!.Element(_maven + "functionAppName")!.Value.Should().Be(new string('a', 60));
    }

    [Theory]
    [InlineData("func-java", "com.example.orders", "com.example.orders", "FunctionHelloWorldSample.java")]
    [InlineData("func-java", "com.class.1orders", "com.pkg_class.pkg1orders", "FunctionHelloWorldSample.java")]
    [InlineData("httptrigger", "com.class.1orders", "com.pkg_class.pkg1orders", "FunctionOrders.java")]
    [InlineData("timertrigger", "java.CON", "pkg_java.pkg_CON", "FunctionOrders.java")]
    [InlineData("httptrigger", "../outside", "_outside", "FunctionOrders.java")]
    public async Task PackageName_IsNormalizedInSourceAndPath(string shortName, string packageName, string expectedPackage, string fileName)
    {
        ITemplateInfo template = await GetTemplateAsync(shortName);

        ITemplateCreationResult result = await GenerateAsync(template, "Orders", new Dictionary<string, string?> { ["PackageName"] = packageName });

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string output = result.OutputBaseDirectory!;
        File.ReadAllText(SourcePath(output, expectedPackage, fileName)).Should().Contain($"package {expectedPackage};");
        if (shortName == "func-java")
        {
            var pom = XDocument.Load(Path.Combine(output, "pom.xml"));
            pom.Root!.Element(_maven + "groupId")!.Value.Should().Be(expectedPackage);
        }
        else
        {
            string expectedPath = $"src/main/java/{expectedPackage.Replace('.', '/')}/{fileName}";
            result.CreationResult!.PrimaryOutputs.Select(path => path.Path.Replace('\\', '/')).Should().Equal(expectedPath);
        }

        AssertNoTemplateArtifacts(output);
    }

    [Theory]
    [InlineData(null, "FUNCTION", CreationResultStatus.Success)]
    [InlineData("anonymous", "ANONYMOUS", CreationResultStatus.Success)]
    [InlineData("admin", "ADMIN", CreationResultStatus.Success)]
    [InlineData("invalid", null, CreationResultStatus.InvalidParamValues)]
    public async Task HttpTrigger_AuthLevel_ValidatesAndGeneratesFunction(string? authLevel, string? expectedAuth, CreationResultStatus expectedStatus)
    {
        ITemplateInfo template = await GetTemplateAsync("httptrigger");
        Dictionary<string, string?> parameters = [];
        if (authLevel is not null)
        {
            parameters["AuthLevel"] = authLevel;
        }

        ITemplateCreationResult result = await GenerateAsync(template, "Get-Orders", parameters);

        result.Status.Should().Be(expectedStatus, result.ErrorMessage);
        if (expectedStatus == CreationResultStatus.Success)
        {
            string output = result.OutputBaseDirectory!;
            string source = File.ReadAllText(SourcePath(output, "com.function", "FunctionGetOrders.java"));
            source.Should().ContainAll(
                "public class FunctionGetOrders", "@FunctionName(\"GetOrders\")",
                "@HttpTrigger(", $"AuthorizationLevel.{expectedAuth}", "HttpMethod.GET", "HttpMethod.POST");
            result.CreationResult!.PrimaryOutputs.Select(path => path.Path.Replace('\\', '/'))
                .Should().Equal("src/main/java/com/function/FunctionGetOrders.java");
            AssertNoTemplateArtifacts(output);
        }
    }

    [Fact]
    public async Task HttpTrigger_AuthSubstitution_PreservesFunctionName()
    {
        ITemplateInfo template = await GetTemplateAsync("httptrigger");

        ITemplateCreationResult result = await GenerateAsync(template, "function", new Dictionary<string, string?> { ["AuthLevel"] = "anonymous" });

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string source = File.ReadAllText(SourcePath(result.OutputBaseDirectory!, "com.function", "FunctionFunction.java"));
        source.Should().ContainAll("@FunctionName(\"Function\")", "AuthorizationLevel.ANONYMOUS");
    }

    [Theory]
    [InlineData("httptrigger", "class", "Class")]
    [InlineData("httptrigger", "COM1", "FunctionCOM1")]
    [InlineData("httptrigger", "lib", "FunctionLib")]
    [InlineData("timertrigger", "CON", "FunctionCON")]
    [InlineData("timertrigger", "host", "FunctionHost")]
    [InlineData("timertrigger", "LIB", "FunctionLIB")]
    public async Task Item_ReservedNames_GenerateUsableNames(string shortName, string name, string expectedName)
    {
        ITemplateInfo template = await GetTemplateAsync(shortName);

        ITemplateCreationResult result = await GenerateAsync(template, name);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string source = File.ReadAllText(SourcePath(result.OutputBaseDirectory!, "com.function", $"Function{expectedName}.java"));
        source.Should().ContainAll($"public class Function{expectedName}", $"@FunctionName(\"{expectedName}\")");
    }

    [Fact]
    public async Task TimerTrigger_GeneratesFunctionWithDefaultSchedule()
    {
        ITemplateInfo template = await GetTemplateAsync("timertrigger");

        ITemplateCreationResult result = await GenerateAsync(template, "1-Cleanup");

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        template.ParameterDefinitions.ContainsKey("Schedule").Should().BeFalse();
        string output = result.OutputBaseDirectory!;
        string source = File.ReadAllText(SourcePath(output, "com.function", "FunctionFunction1Cleanup.java"));
        source.Should().ContainAll(
            "public class FunctionFunction1Cleanup", "@FunctionName(\"Function1Cleanup\")",
            "@TimerTrigger(name = \"timerInfo\", schedule = \"0 */5 * * * *\")", "ExecutionContext");
        result.CreationResult!.PrimaryOutputs.Select(path => path.Path.Replace('\\', '/'))
            .Should().Equal("src/main/java/com/function/FunctionFunction1Cleanup.java");
        AssertNoTemplateArtifacts(output);
    }

    [Fact]
    public async Task ProjectAndItems_CoexistWithoutOverwritingFunctions()
    {
        ITemplateInfo project = await GetTemplateAsync("func-java");
        ITemplateInfo http = await GetTemplateAsync("httptrigger");
        ITemplateInfo timer = await GetTemplateAsync("timertrigger");
        Dictionary<string, string?> parameters = new() { ["PackageName"] = "com.example.functions" };
        ITemplateCreationResult projectResult = await GenerateAsync(project, "App", parameters);
        projectResult.Status.Should().Be(CreationResultStatus.Success, projectResult.ErrorMessage);
        string output = projectResult.OutputBaseDirectory!;
        string helloPath = SourcePath(output, "com.example.functions", "FunctionHelloWorldSample.java");
        string hello = File.ReadAllText(helloPath);

        ITemplateCreationResult first = await GenerateAsync(http, "Orders", parameters, output);
        ITemplateCreationResult second = await GenerateAsync(timer, "Cleanup", parameters, output);
        ITemplateCreationResult third = await GenerateAsync(http, "Health", parameters, output);
        ITemplateCreationResult helloItem = await GenerateAsync(http, "HelloWorld", parameters, output);

        first.Status.Should().Be(CreationResultStatus.Success, first.ErrorMessage);
        second.Status.Should().Be(CreationResultStatus.Success, second.ErrorMessage);
        third.Status.Should().Be(CreationResultStatus.Success, third.ErrorMessage);
        helloItem.Status.Should().Be(CreationResultStatus.Success, helloItem.ErrorMessage);
        first.CreationEffects!.FileChanges.Should().OnlyContain(change => change.ChangeKind == ChangeKind.Create);
        second.CreationEffects!.FileChanges.Should().OnlyContain(change => change.ChangeKind == ChangeKind.Create);
        third.CreationEffects!.FileChanges.Should().OnlyContain(change => change.ChangeKind == ChangeKind.Create);
        File.ReadAllText(helloPath).Should().Be(hello);
        File.ReadAllText(SourcePath(output, "com.example.functions", "FunctionOrders.java")).Should().Contain("@FunctionName(\"Orders\")");
        File.ReadAllText(SourcePath(output, "com.example.functions", "FunctionCleanup.java")).Should().Contain("@FunctionName(\"Cleanup\")");
        File.ReadAllText(SourcePath(output, "com.example.functions", "FunctionHealth.java")).Should().Contain("@FunctionName(\"Health\")");
        File.ReadAllText(SourcePath(output, "com.example.functions", "FunctionHelloWorld.java")).Should().Contain("@FunctionName(\"HelloWorld\")");
        AssertNoTemplateArtifacts(output);
    }

    [Theory]
    [InlineData("httptrigger", "get-orders", "GetOrders")]
    [InlineData("httptrigger", "GetOrders", "GetOrders")]
    [InlineData("timertrigger", "clean_up", "CleanUp")]
    [InlineData("timertrigger", "HTTPTrigger", "HTTPTrigger")]
    public async Task Item_Name_UsesJavaStyleCasing(string shortName, string name, string expectedName)
    {
        ITemplateInfo template = await GetTemplateAsync(shortName);

        ITemplateCreationResult result = await GenerateAsync(template, name);

        result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
        string source = File.ReadAllText(SourcePath(result.OutputBaseDirectory!, "com.function", $"Function{expectedName}.java"));
        source.Should().ContainAll($"public class Function{expectedName}", $"@FunctionName(\"{expectedName}\")");
    }

    [Theory]
    [InlineData("httptrigger")]
    [InlineData("timertrigger")]
    public async Task Item_Name_UsesConsistentCasingInTurkishCulture(string shortName)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            ITemplateInfo template = await GetTemplateAsync(shortName);

            ITemplateCreationResult result = await GenerateAsync(template, "item-id");

            result.Status.Should().Be(CreationResultStatus.Success, result.ErrorMessage);
            string source = File.ReadAllText(SourcePath(result.OutputBaseDirectory!, "com.function", "FunctionItemId.java"));
            source.Should().Contain("@FunctionName(\"ItemId\")");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static string SourcePath(string output, string packageName, string fileName)
        => Path.Combine([output, "src", "main", "java", .. packageName.Split('.'), fileName]);

    private static void AssertNoTemplateArtifacts(string output)
    {
        Directory.EnumerateDirectories(output, ".template.config", SearchOption.AllDirectories).Should().BeEmpty();
        foreach (string file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
        {
            file.Should().NotContain("TemplatePackagePath").And.NotContain("TemplateClassName");
            File.ReadAllText(file).Should().NotContainAny("#if", "#endif", "TemplatePackagePath", "TemplateClassName", "TemplateFunctionName");
        }
    }

    private Task<ITemplateInfo> GetTemplateAsync(string shortName)
        => _host.GetTemplateAsync(shortName, "Java", TestContext.Current.CancellationToken);

    private Task<ITemplateCreationResult> GenerateAsync(
        ITemplateInfo template, string name, IReadOnlyDictionary<string, string?>? parameters = null, string? outputDirectory = null)
        => _host.InstantiateAsync(template, name, parameters, outputDirectory, cancellationToken: TestContext.Current.CancellationToken);
}
