// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Stub template provider. Exposes a single HttpTrigger template and writes a
/// minimal C# function file. Demonstrates the <see cref="ITemplateProvider"/>
/// extension shape; the real provider will delegate to <c>dotnet new</c>.
/// </summary>
public sealed class DotnetTemplateProvider : ITemplateProvider
{
    private static readonly FunctionTemplate[] _templates =
    [
        new("HttpTrigger", "C# HTTP-triggered function (isolated worker)", "dotnet"),
    ];

    public string WorkerRuntime => "dotnet";

    public bool CanHandle(string workerRuntime) =>
        workerRuntime.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
        workerRuntime.Equals("dotnet-isolated", StringComparison.OrdinalIgnoreCase) ||
        workerRuntime.Equals("csharp", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<Option> GetNewOptions() => [];

    public Task<IReadOnlyList<FunctionTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FunctionTemplate>>(_templates);

    public async Task ScaffoldAsync(
        FunctionScaffoldContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        if (!_templates.Any(t => t.Name.Equals(context.TemplateName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"Unknown dotnet template '{context.TemplateName}'. Run 'func new -w dotnet' to list templates.",
                nameof(context));
        }

        Directory.CreateDirectory(context.OutputPath);

        var content = $$"""
            using Microsoft.Azure.Functions.Worker;
            using Microsoft.Azure.Functions.Worker.Http;
            using Microsoft.Extensions.Logging;

            namespace MyFunctionApp;

            public class {{context.FunctionName}}
            {
                private readonly ILogger _logger;

                public {{context.FunctionName}}(ILoggerFactory loggerFactory)
                {
                    _logger = loggerFactory.CreateLogger<{{context.FunctionName}}>();
                }

                [Function("{{context.FunctionName}}")]
                public HttpResponseData Run(
                    [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
                {
                    var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                    response.WriteString("Welcome to Azure Functions!");
                    return response;
                }
            }
            """;

        var path = Path.Combine(context.OutputPath, $"{context.FunctionName}.cs");
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }
}
