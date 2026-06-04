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

    public Task<ProjectCreationResult> TryCreateProjectAsync(ProjectCreationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo workingDirectory = context.WorkingDirectory.Info;
        if (!workingDirectory.Exists)
        {
            return Task.FromResult(NotCreated("directory does not exist"));
        }

        (string? reason, string language) = Fingerprint(workingDirectory);
        if (reason is null)
        {
            return Task.FromResult(NotCreated("no Node project fingerprint found"));
        }

        FunctionsProject project = new NodeFunctionsProject(context.WorkingDirectory, language);
        return Task.FromResult(Created(project, reason));
    }

    // Language defaults to JavaScript so a Node directory with only package.json
    // (no .ts files) still classifies cleanly.
    private static (string? Reason, string Language) Fingerprint(DirectoryInfo workingDirectory)
    {
        string root = workingDirectory.FullName;
        bool hasTsConfig = File.Exists(Path.Combine(root, "tsconfig.json"));
        bool hasTopLevelTs = hasTsConfig
            || Directory.EnumerateFiles(root, "*.ts", SearchOption.TopDirectoryOnly).Any();
        string language = hasTopLevelTs ? "TypeScript" : "JavaScript";

        string packageJsonPath = Path.Combine(root, "package.json");
        if (File.Exists(packageJsonPath))
        {
            string reason = DeclaresFunctionsPackage(packageJsonPath)
                ? "package.json declares @azure/functions"
                : "found package.json";
            return (reason, language);
        }

        if (hasTsConfig)
        {
            return ("found tsconfig.json", language);
        }

        foreach (string pattern in _sourceFilePatterns)
        {
            if (Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly).Any())
            {
                return ($"found {pattern} file", language);
            }
        }

        return (null, language);
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
}
