// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using static Azure.Functions.Cli.Projects.ProjectCreationResults;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Creates Node Functions projects from Node-specific fingerprints.
/// </summary>
internal sealed class NodeProjectFactory : IFunctionsProjectFactory
{
    private const string FunctionsPackage = "@azure/functions";

    private static readonly FunctionsWorkerId _workerId = new("node");
    private static readonly string[] _sourceFilePatterns = ["*.js", "*.mjs", "*.cjs", "*.ts"];

    public async Task<ProjectCreationResult> TryCreateProjectAsync(ProjectCreationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.WorkerResolver);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo workingDirectory = context.WorkingDirectory.Info;
        if (!workingDirectory.Exists)
        {
            return NotCreated("directory does not exist");
        }

        string? reason = TryGetReason(workingDirectory);
        if (reason is null)
        {
            return NotCreated("no Node project fingerprint found");
        }

        FunctionsWorkerResolutionResult workerResult =
            await context.WorkerResolver.ResolveWorkerAsync(_workerId, cancellationToken);
        return workerResult switch
        {
            FunctionsWorkerResolutionResult.Resolved resolved => Created(new NodeFunctionsProject(context.WorkingDirectory, resolved.Worker), reason),
            FunctionsWorkerResolutionResult.NotResolved notResolved => Failed(ProjectCreationFailures.WorkerNotResolved(notResolved.Failure)),
            _ => throw new InvalidOperationException($"Unsupported worker resolution result: {workerResult.GetType().FullName}"),
        };
    }

    private static string? TryGetReason(DirectoryInfo workingDirectory)
    {
        string packageJsonPath = Path.Combine(workingDirectory.FullName, "package.json");
        if (File.Exists(packageJsonPath))
        {
            return DeclaresFunctionsPackage(packageJsonPath)
                ? "package.json declares @azure/functions"
                : "found package.json";
        }

        if (File.Exists(Path.Combine(workingDirectory.FullName, "tsconfig.json")))
        {
            return "found tsconfig.json";
        }

        foreach (string pattern in _sourceFilePatterns)
        {
            if (Directory.EnumerateFiles(workingDirectory.FullName, pattern, SearchOption.TopDirectoryOnly).Any())
            {
                return $"found {pattern} file";
            }
        }

        return null;
    }

    private static bool DeclaresFunctionsPackage(string packageJsonPath)
    {
        // Parse just enough of package.json to spot @azure/functions in
        // dependencies or devDependencies. Malformed JSON falls through
        // to the weaker "found package.json" signal rather than throwing.
        try
        {
            using FileStream stream = File.OpenRead(packageJsonPath);
            using var doc = JsonDocument.Parse(stream);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return HasFunctionsDependency(doc.RootElement, "dependencies")
                || HasFunctionsDependency(doc.RootElement, "devDependencies");
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool HasFunctionsDependency(JsonElement root, string sectionName)
    {
        return root.TryGetProperty(sectionName, out JsonElement section)
            && section.ValueKind == JsonValueKind.Object
            && section.TryGetProperty(FunctionsPackage, out _);
    }

    private sealed record NodeFunctionsProject(WorkingDirectory WorkingDirectory, IFunctionsWorker Worker) : IFunctionsProject
    {
        public string StackName => "node";

        public string StackDisplayName => "Node.js";

        public bool SupportsExtensionBundles => true;
    }
}
