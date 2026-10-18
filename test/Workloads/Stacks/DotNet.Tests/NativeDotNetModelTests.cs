// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed partial class NativeDotNetTemplateTests
{
    [Theory]
    [InlineData("Azure.Function.CSharp.Isolated.CosmosDBTrigger.4.x", "Document", "id", "Text")]
    [InlineData("Azure.Function.CSharp.KustoOutputBindingIsolated", "Item", "Id", "Description")]
    [InlineData("Azure.Function.CSharp.Isolated.SignalR.3.x", "ConnectionInfo", "Url", "AccessToken")]
    [InlineData("Azure.Function.CSharp.Isolated.SqlOutputBinding", "Item", "Id", "Description")]
    [InlineData("Azure.Function.CSharp.Isolated.SqlTriggerBinding", "Item", "Id", "Description")]
    [InlineData("Azure.Function.CSharp.Isolated.MySqlOutputBinding", "Item", "Id", "Description")]
    [InlineData("Azure.Function.CSharp.Isolated.MySqlTriggerBinding", "Item", "Id", "Description")]
    public async Task NativeModels_OwnedProperties_DeclareExistingNullDefaultsWithoutInitializers(
        string identity, string suffix, string firstProperty, string secondProperty)
    {
        using NativeSession session = await OpenNativeSessionAsync();
        ITemplateInfo template = (await session.Manager.GetTemplatesAsync(CancellationToken.None)).Single(item => item.Identity == identity);
        TemplateCreator creator = new(session.Settings);
        string output = Path.Combine(_root, "models");
        foreach (string name in new[] { "First", "Second" })
        {
            ITemplateCreationResult created = await creator.InstantiateAsync(template, name, name, output,
                new Dictionary<string, string?> { ["namespace"] = "Custom.Functions" }, cancellationToken: CancellationToken.None);

            created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
            string source = File.ReadAllText(Path.Combine(output, name + ".cs"));
            source.Should().ContainAll($"    public class {name}{suffix}", $"public string? {firstProperty} {{ get; set; }}",
                $"public string? {secondProperty} {{ get; set; }}");
            source.Should().NotContain("= string.Empty").And.NotContain("= null!").And.NotContain("required string");
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NativeModels_RepeatedOwnedModels_BuildStrictlyAndPreserveActualNullAndJsonSemantics()
    {
        using NativeSession session = await OpenNativeSessionAsync();
        IReadOnlyList<ITemplateInfo> templates = await session.Manager.GetTemplatesAsync(CancellationToken.None);
        TemplateCreator creator = new(session.Settings);
        string output = Path.Combine(_root, "strict-models");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
        ITemplateInfo projectTemplate = templates.Single(template =>
            template.Identity == "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x");
        ITemplateCreationResult projectResult = await creator.InstantiateAsync(projectTemplate, "Sample", "Sample", output,
            new Dictionary<string, string?> { ["Framework"] = "net10.0" }, cancellationToken: timeout.Token);
        projectResult.Status.Should().Be(CreationResultStatus.Success, projectResult.ErrorMessage);
        List<IPostAction> actions = [.. projectResult.CreationEffects!.CreationResult.PostActions];
        (string Identity, string Name, string Suffix)[] cases =
        [
            ("Azure.Function.CSharp.Isolated.CosmosDBTrigger.4.x", "Cosmos", "Document"),
            ("Azure.Function.CSharp.KustoOutputBindingIsolated", "Kusto", "Item"),
            ("Azure.Function.CSharp.Isolated.SignalR.3.x", "Signal", "ConnectionInfo"),
            ("Azure.Function.CSharp.Isolated.SqlOutputBinding", "SqlOutput", "Item"),
            ("Azure.Function.CSharp.Isolated.SqlTriggerBinding", "SqlTrigger", "Item"),
            ("Azure.Function.CSharp.Isolated.MySqlOutputBinding", "MySqlOutput", "Item"),
            ("Azure.Function.CSharp.Isolated.MySqlTriggerBinding", "MySqlTrigger", "Item"),
        ];
        List<(string Function, string Model)> expected = [];
        foreach ((string identity, string prefix, string suffix) in cases)
        {
            ITemplateInfo template = templates.Single(item => item.Identity == identity);
            foreach (string ordinal in new[] { "First", "Second" })
            {
                string name = prefix + ordinal;
                ITemplateCreationResult created = await creator.InstantiateAsync(template, name, name, output,
                    new Dictionary<string, string?> { ["namespace"] = "Sample" }, cancellationToken: timeout.Token);
                created.Status.Should().Be(CreationResultStatus.Success, created.ErrorMessage);
                actions.AddRange(created.CreationEffects!.CreationResult.PostActions);
                expected.Add((name, $"Sample.{name}+{name}{suffix}"));
            }
        }
        string custom = Path.Combine(output, "Custom.cs");
        const string userSource = "namespace Sample; public class ToDoItem { } public class MyDocument { } "
            + "public class CosmosFirstDocument { } public class KustoFirstItem { } public class MyConnectionInfo { }";
        File.WriteAllText(custom, userSource);
        DirectoryInfo repository = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "NuGet.Config")))
        {
            repository = repository.Parent ?? throw new InvalidOperationException("The repository NuGet.Config was not found.");
        }
        File.Copy(Path.Combine(repository.FullName, "NuGet.Config"), Path.Combine(output, "NuGet.Config"));
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(resolver);
        DotNetTemplateDependencyExecutor dependencies = new(runner);
        string project = Path.Combine(output, "Sample.csproj");
        foreach (IPostAction action in actions.Where(action => action.ActionId == _packageAction)
            .DistinctBy(action => (action.Args["reference"], action.Args["version"])))
        {
            await dependencies.AddPackageReferenceAsync(project,
                new(action.Args["reference"], action.Args["version"], action.Args["projectFileExtensions"]), timeout.Token);
        }
        await dependencies.RestoreAsync(project, timeout.Token);

        await runner.RunAsync(["build", project, "--no-restore", "-t:Rebuild", "-p:TreatWarningsAsErrors=true", "-m:1", "-v:minimal"],
            output, timeout.Token);

        string built = Path.Combine(output, "bin", "Debug", "net10.0");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(built, "functions.metadata")));
        metadata.RootElement.EnumerateArray().Select(function => function.GetProperty("name").GetString())
            .Should().BeEquivalentTo(expected.Select(item => item.Function)).And.OnlyHaveUniqueItems();
        File.ReadAllText(custom).Should().Be(userSource);
        AssemblyLoadContext load = new("native-model-semantics", isCollectible: true);
        load.Resolving += (context, name) =>
        {
            string path = Path.Combine(built, name.Name + ".dll");
            if (!File.Exists(path))
            {
                return null;
            }
            using FileStream dependency = File.OpenRead(path);
            return context.LoadFromStream(dependency);
        };
        try
        {
            using FileStream assemblyStream = File.OpenRead(Path.Combine(built, "Sample.dll"));
            Assembly assembly = load.LoadFromStream(assemblyStream);
            NullabilityInfoContext nullability = new();
            foreach ((_, string model) in expected)
            {
                Type type = assembly.GetType(model, throwOnError: true)!;
                object instance = Activator.CreateInstance(type)!;
                PropertyInfo[] strings = [.. type.GetProperties().Where(property => property.PropertyType == typeof(string))];
                strings.Should().HaveCount(2);
                using var defaults = JsonDocument.Parse(JsonSerializer.Serialize(instance, type));
                foreach (PropertyInfo property in strings)
                {
                    nullability.Create(property).ReadState.Should().Be(NullabilityState.Nullable);
                    property.GetValue(instance).Should().BeNull();
                    defaults.RootElement.GetProperty(property.Name).ValueKind.Should().Be(JsonValueKind.Null);
                    string explicitNull = "{\"" + property.Name + "\":null}";
                    property.GetValue(JsonSerializer.Deserialize("{}", type)).Should().BeNull();
                    property.GetValue(JsonSerializer.Deserialize(explicitNull, type)).Should().BeNull();
                    property.GetValue(Newtonsoft.Json.JsonConvert.DeserializeObject("{}", type)).Should().BeNull();
                    property.GetValue(Newtonsoft.Json.JsonConvert.DeserializeObject(explicitNull, type)).Should().BeNull();
                    property.SetValue(instance, property.Name + " \"\u96EA\"");
                }
                string systemJson = JsonSerializer.Serialize(instance, type);
                string newtonsoftJson = Newtonsoft.Json.JsonConvert.SerializeObject(instance);
                JsonNode.DeepEquals(JsonNode.Parse(systemJson), JsonNode.Parse(newtonsoftJson)).Should().BeTrue();
            }
        }
        finally
        {
            load.Unload();
        }
    }
}
