// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.DotNet;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates.DotNet;

public class DotNetEngineProviderTests : IDisposable
{
    private readonly string _installDir;

    public DotNetEngineProviderTests()
    {
        _installDir = Path.Combine(Path.GetTempPath(), "func-new-pr3-dotnet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_installDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task ListTemplatesAsync_Empty_Registry_Returns_Empty()
    {
        IInstalledTemplatesWorkloads installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("dotnet", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<InstalledTemplatesWorkload>());

        IDotnetTemplateRunner runner = Substitute.For<IDotnetTemplateRunner>();
        var provider = new DotNetEngineProvider(installed, runner);

        IReadOnlyList<FunctionTemplateInfo> templates = await provider.ListTemplatesAsync(
            new TemplateListContext(new Cli.Common.WorkingDirectory(new DirectoryInfo(_installDir), false), "dotnet", "csharp"),
            CancellationToken.None);

        Assert.Empty(templates);
    }

    [Fact]
    public async Task ListTemplatesAsync_Dedupes_CSharp_FSharp_By_GroupIdentity()
    {
        WriteFixture(_installDir);

        IInstalledTemplatesWorkloads installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("dotnet", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("dotnet", "1.0.0", _installDir)]);

        IDotnetTemplateRunner runner = Substitute.For<IDotnetTemplateRunner>();
        var provider = new DotNetEngineProvider(installed, runner);

        IReadOnlyList<FunctionTemplateInfo> templates = await provider.ListTemplatesAsync(
            new TemplateListContext(new Cli.Common.WorkingDirectory(new DirectoryInfo(_installDir), false), "dotnet", null),
            CancellationToken.None);

        // Two records (C# + F# variants of HttpTrigger sharing groupIdentity)
        // collapse to a single FunctionTemplateInfo whose Languages list both.
        var info = Assert.Single(templates);
        Assert.Equal("http", info.Id);
        Assert.Equal(EngineIds.DotNet, info.EngineId);
        Assert.Contains("c#", info.Languages, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("f#", info.Languages, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListTemplatesAsync_Filters_By_Language()
    {
        WriteFixture(_installDir);

        IInstalledTemplatesWorkloads installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("dotnet", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("dotnet", "1.0.0", _installDir)]);

        IDotnetTemplateRunner runner = Substitute.For<IDotnetTemplateRunner>();
        var provider = new DotNetEngineProvider(installed, runner);

        // Stack-language "csharp" should accept the deduped HttpTrigger record
        // since C# is one of its supported languages.
        IReadOnlyList<FunctionTemplateInfo> csharp = await provider.ListTemplatesAsync(
            new TemplateListContext(new Cli.Common.WorkingDirectory(new DirectoryInfo(_installDir), false), "dotnet", "csharp"),
            CancellationToken.None);

        Assert.Single(csharp);
    }

    [Fact]
    public async Task ApplyAsync_Successful_Exit_Returns_Created()
    {
        WriteFixture(_installDir);

        IInstalledTemplatesWorkloads installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("dotnet", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("dotnet", "1.0.0", _installDir)]);

        IDotnetTemplateRunner runner = Substitute.For<IDotnetTemplateRunner>();
        runner.RunAsync(
                Arg.Any<string>(),
                Arg.Any<DirectoryInfo>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(new DotnetTemplateRunResult(0, "Created file Function1.cs", string.Empty));

        var provider = new DotNetEngineProvider(installed, runner);

        FunctionTemplateInfo info = new(
            Id: "http",
            Stack: "dotnet",
            EngineId: EngineIds.DotNet,
            DisplayName: "HttpTrigger",
            Description: null,
            DefaultFunctionName: null,
            Languages: ["c#"],
            Metadata: new TemplateMetadata([], RequiresExtensionBundle: false, MinBundleVersion: null));

        TemplateApplicationResult result = await provider.ApplyAsync(
            new NewContext(
                new Cli.Common.WorkingDirectory(new DirectoryInfo(_installDir), false),
                info,
                FunctionName: "Function1",
                Language: "csharp",
                Force: false),
            new System.CommandLine.RootCommand().Parse(string.Empty),
            CancellationToken.None);

        Assert.IsType<TemplateApplicationResult.Created>(result);
    }

    [Fact]
    public async Task ApplyAsync_NonZero_Exit_Returns_ProviderError()
    {
        WriteFixture(_installDir);

        IInstalledTemplatesWorkloads installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("dotnet", Arg.Any<CancellationToken>())
            .Returns([new InstalledTemplatesWorkload("dotnet", "1.0.0", _installDir)]);

        IDotnetTemplateRunner runner = Substitute.For<IDotnetTemplateRunner>();
        runner.RunAsync(
                Arg.Any<string>(),
                Arg.Any<DirectoryInfo>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(new DotnetTemplateRunResult(2, string.Empty, "Template 'http' could not be found."));

        var provider = new DotNetEngineProvider(installed, runner);

        FunctionTemplateInfo info = new(
            Id: "http",
            Stack: "dotnet",
            EngineId: EngineIds.DotNet,
            DisplayName: "HttpTrigger",
            Description: null,
            DefaultFunctionName: null,
            Languages: ["c#"],
            Metadata: new TemplateMetadata([], RequiresExtensionBundle: false, MinBundleVersion: null));

        TemplateApplicationResult result = await provider.ApplyAsync(
            new NewContext(
                new Cli.Common.WorkingDirectory(new DirectoryInfo(_installDir), false),
                info,
                FunctionName: "Fn",
                Language: "csharp",
                Force: false),
            new System.CommandLine.RootCommand().Parse(string.Empty),
            CancellationToken.None);

        var failed = Assert.IsType<TemplateApplicationResult.Failed>(result);
        Assert.IsType<TemplateApplicationFailure.ProviderError>(failed.Failure);
    }

    private static void WriteFixture(string installDir)
    {
        string contentDir = Path.Combine(installDir, "tools", "any", "content");
        Directory.CreateDirectory(contentDir);

        File.WriteAllText(Path.Combine(contentDir, "dotnet-templates.json"), """
        {
          "$schema": "https://aka.ms/func-workloads/dotnet-templates/v1/schema.json",
          "sourcePackage": { "id": "Microsoft.Azure.Functions.Worker.ItemTemplates.NetCore", "version": "4.0.5569" },
          "templates": [
            {
              "id": "http",
              "shortNames": ["http"],
              "identity": "Azure.Function.CSharp.HttpTrigger.2.x",
              "groupIdentity": "Azure.Function.HttpTrigger",
              "name": "HttpTrigger",
              "description": "Creates an HTTP-triggered function.",
              "language": "C#",
              "type": "item",
              "classifications": ["Azure Function", "Trigger", "Http"],
              "defaultName": "HttpTriggerCSharp",
              "parameters": [
                {
                  "name": "namespace",
                  "description": "namespace for the generated code",
                  "dataType": "string",
                  "defaultValue": "Company.Function",
                  "isRequired": false,
                  "isHidden": false
                }
              ]
            },
            {
              "id": "http",
              "shortNames": ["http"],
              "identity": "Azure.Function.FSharp.HttpTrigger.2.x",
              "groupIdentity": "Azure.Function.HttpTrigger",
              "name": "HttpTrigger",
              "description": "Creates an HTTP-triggered function.",
              "language": "F#",
              "type": "item",
              "classifications": ["Azure Function", "Trigger", "Http"],
              "defaultName": "HttpTriggerFSharp"
            }
          ]
        }
        """);

        File.WriteAllText(Path.Combine(contentDir, "source.json"), """
        { "kind": "nuget", "packageId": "Microsoft.Azure.Functions.Worker.ItemTemplates.NetCore", "version": "4.0.5569" }
        """);
    }
}
