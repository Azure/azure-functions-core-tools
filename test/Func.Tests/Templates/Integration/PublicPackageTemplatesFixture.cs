// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Tests.Templates.PackageIntake;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Integration;

/// <summary>
/// Runs the public parser with real Node inspection and native rendering against a synthetic package with the qualified metadata shape.
/// </summary>
internal sealed class PublicPackageTemplatesFixture : IDisposable
{
    internal const string ProjectIdentity = "Fixture.Node.Project.JavaScript";
    internal const string ItemIdentity = "Fixture.Node.Http.JavaScript";
    internal const string PublicHttp = "HttpTrigger-JavaScript";
    internal const string ResourceName = "Fixture.Qualification.Node.nupkg";
    private readonly IServiceProvider _provider;

    public PublicPackageTemplatesFixture(
        bool enabled = true, string? defaultProject = null, string? expectedId = null, string? expectedVersion = null,
        byte[]? packageBytes = null, string? resourceName = null)
    {
        Root = Directory.CreateTempSubdirectory("func-public-templates-").FullName;
        Bytes = packageBytes ?? Archive();
        Resources.OpenRead(Arg.Any<string>()).Returns(call =>
        {
            ResourceReads.Add(call.Arg<string>());
            if (call.Arg<string>() != ResourceName)
            {
                throw new FileNotFoundException("The selected resource name does not exist.", call.Arg<string>());
            }

            if (ResourceFailure is not null)
            {
                throw ResourceFailure;
            }

            var stream = new ResourceStream(Bytes);
            Readers.Add(stream);
            return stream;
        });
        LegacyCatalog.ListAsync(Arg.Any<NewCommandResolvedContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<FunctionTemplateInfo>>(
                [LegacyTemplate(PublicHttp), LegacyTemplate("QueueTrigger-JavaScript")]));
        LegacyApplicator.ApplyAsync(Arg.Any<NewInvocation>(), Arg.Any<NewCommandResolvedContext>(),
            Arg.Any<FunctionTemplateInfo>(), Arg.Any<CancellationToken>())
            .Returns(new TemplateApplicationResult.Created(["legacy-output.js"]));
        LegacyContext.ResolveAsync(Arg.Any<NewInvocation>(), Arg.Any<CancellationToken>())
            .Returns(call => NewCommandResolutionResult.Succeed(new(
                call.ArgAt<NewInvocation>(0).WorkingDirectory, "node", "javascript",
                new("node", "1.0.0", Root), null, BundleChannel.Unknown, false)));

        var binding = new PublicTemplatePackageBinding(
            new(expectedId ?? TemplatePackageTestData.PackageId, expectedVersion ?? TemplatePackageTestData.PackageVersion,
                "FuncTemplate", Convert.ToHexStringLower(SHA256.HashData(Bytes))),
            resourceName ?? ResourceName, defaultProject ?? ProjectIdentity, ItemIdentity, PublicHttp);
        _provider = TestParser.BuildServiceProviderWith(Interaction, services =>
        {
            new NodeWorkload().Configure(new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo("Fixture.Node")));
            services.AddSingleton<IOptions<PublicTemplatePackageOptions>>(Options.Create(new PublicTemplatePackageOptions
            {
                Qualification = enabled ? binding : null,
            }));
            services.AddSingleton(Resources);
            services.AddSingleton(LegacyContext);
            services.AddSingleton(LegacyCatalog);
            services.AddSingleton(LegacyApplicator);
            var monitor = Substitute.For<IOptionsMonitor<StackOptions>>();
            monitor.Get(Arg.Any<string>()).Returns(call => ReadStack(call.Arg<string>()));
            services.AddSingleton(monitor);
            services.AddSingleton<IFunctionsProjectResolver>(provider =>
            {
                var resolver = new FunctionsProjectResolver(provider.GetServices<WorkloadProjectFactoryRegistration>());
                var boundary = Substitute.For<IFunctionsProjectResolver>();
                boundary.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                    .Returns(call =>
                    {
                        var context = call.ArgAt<ProjectResolutionContext>(0);
                        var token = call.ArgAt<CancellationToken>(1);
                        Resolutions.Add(context);
                        if (context.InspectCapabilities && InspectionFailure is not null)
                        {
                            return Task.FromException<ProjectResolutionResult>(InspectionFailure);
                        }

                        return resolver.ResolveProjectAsync(context, token);
                    });
                return boundary;
            });
        });
        Command = Parser.CreateCommand(_provider);
    }

    public string Root { get; }

    public byte[] Bytes { get; set; }

    public Exception? ResourceFailure { get; set; }

    public Exception? InspectionFailure { get; set; }

    public TestInteractionService Interaction { get; } = new();

    public ITemplatePackageResources Resources { get; } = Substitute.For<ITemplatePackageResources>();

    public INewCommandContextResolver LegacyContext { get; } = Substitute.For<INewCommandContextResolver>();

    public INewCommandTemplateCatalog LegacyCatalog { get; } = Substitute.For<INewCommandTemplateCatalog>();

    public INewCommandTemplateApplicator LegacyApplicator { get; } = Substitute.For<INewCommandTemplateApplicator>();

    public List<string> ResourceReads { get; } = [];

    public List<ResourceStream> Readers { get; } = [];

    public List<ProjectResolutionContext> Resolutions { get; } = [];

    public FuncRootCommand Command { get; }

    public string Target => Path.Combine(Root, "project");

    public IPublicPackageTemplates Packages => _provider.GetRequiredService<IPublicPackageTemplates>();

    public InitCommand Init => Command.Subcommands.OfType<InitCommand>().Single();

    public NewCommand New => Command.Subcommands.OfType<NewCommand>().Single();

    public async Task<int> InvokeAsync(params string[] args)
    {
        NewCommandArgPreparer.PrepareIfFuncNew(args, _provider, Command);
        return await Command.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
    }

    public void WriteProject(string language = "JavaScript")
    {
        Write("package.json", """{"dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js"}""");
        Write("host.json", """{"version":"2.0","extensionBundle":{"id":"Microsoft.Azure.Functions.ExtensionBundle","version":"[4.*,5.0.0)"}}""");
        Write(".func/config.json", $$$"""{"stack":{"runtime":"node","language":"{{{language}}}"},"custom":{"preserve":true}}""");
        if (language == "TypeScript")
        {
            Write("tsconfig.json", "{}");
        }
    }

    public void Write(string logicalPath, string content)
    {
        string path = Path.Combine(Target, logicalPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public Dictionary<string, string> Snapshot()
        => Directory.Exists(Target) ? Directory.GetFiles(Target, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Target, path), File.ReadAllText, StringComparer.Ordinal) : [];

    public void Dispose()
    {
        (_provider as IDisposable)?.Dispose();
        Directory.Delete(Root, recursive: true);
    }

    public static byte[] Archive(string? projectIdentity = null, string? itemExtra = null, bool hidden = false, string? requirement = null)
    {
        string project = $$$"""
            {"identity":"{{{projectIdentity ?? ProjectIdentity}}}","name":"Node project","shortName":"func-node-js",
             "tags":{"type":"project","language":"JavaScript"},
             "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]}},
             "primaryOutputs":[{"path":"host.json"}],
             "postActions":[{"actionId":"00e33184-ffc7-43ef-bec0-b1684df8ad56","continueOnError":false,
               "args":{"primaryOutputIndex":"0","stack":"node","language":"JavaScript"}}]}
            """;
        string item = $$$"""
            {"identity":"{{{ItemIdentity}}}","name":"HTTP trigger","shortName":"func-http-js",
             "tags":{"type":"item","language":"JavaScript"},"sourceName":"httpTrigger","defaultName":"httpTrigger",
             "sources":[{"source":"./","target":"src/functions/"}],
             "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]}},
             "funcProjectCapabilities":{{{requirement ?? """{"type":"func-project-capabilities","args":["azure.functions.node.model-v4.javascript-commonjs"]}"""}}}
             {{{itemExtra}}}}
            """;
        List<TemplatePackageTestData.Entry> entries =
        [
            new("content/project/.template.config/template.json", project),
            new("content/project/package.json", """{"name":"qualification-node","dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js"}"""),
            new("content/project/host.json", """{"version":"2.0"}"""),
            new("content/project/local.settings.json", """{"IsEncrypted":false,"Values":{"FUNCTIONS_WORKER_RUNTIME":"node","AzureWebJobsStorage":""}}"""),
            new("content/http/.template.config/template.json", item),
            new("content/http/httpTrigger.js", "const { app } = require('@azure/functions');\napp.http('httpTrigger', { handler: async () => ({ body: 'Hello' }) });"),
        ];
        if (hidden)
        {
            entries.Add(new("content/http/.template.config/func.host.json", """{"isHidden":true}"""));
        }

        return TemplatePackageTestData.Archive(entries);
    }

    private StackOptions ReadStack(string directory)
    {
        string path = Path.Combine(directory, ".func", "config.json");
        if (!File.Exists(path))
        {
            return new();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement stack = document.RootElement.GetProperty("stack");
        return new()
        {
            Runtime = stack.TryGetProperty("runtime", out JsonElement runtime) ? runtime.GetString() : null,
            Language = stack.TryGetProperty("language", out JsonElement language) ? language.GetString() : null,
        };
    }

    private static FunctionTemplateInfo LegacyTemplate(string id)
        => new(id, "node", EngineIds.V2, id, null, "legacyDefault", ["javascript"], new([], false, null));

    internal sealed class ResourceStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public int CloseCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            CloseCount++;
            base.Dispose(disposing);
        }
    }
}
