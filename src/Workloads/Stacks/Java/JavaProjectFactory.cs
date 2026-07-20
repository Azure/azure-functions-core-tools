// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using static Azure.Functions.Cli.Projects.ProjectCreationResults;

namespace Azure.Functions.Cli.Workloads.Java;

/// <summary>
/// Creates Java Functions projects from Java-specific fingerprints
/// (Maven <c>pom.xml</c>, Gradle build scripts, or a <c>src/main/java</c> tree).
/// </summary>
internal sealed class JavaProjectFactory : IFunctionsProjectFactory
{
    public Task<ProjectCreationResult> TryCreateProjectAsync(ProjectCreationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        DirectoryInfo workingDirectory = context.WorkingDirectory.Info;
        if (!workingDirectory.Exists)
        {
            return Task.FromResult(NotCreated("directory does not exist"));
        }

        string? reason = TryGetReason(workingDirectory);
        if (reason is null)
        {
            return Task.FromResult(NotCreated("no Java project fingerprint found"));
        }

        FunctionsProject project = new JavaFunctionsProject(context.WorkingDirectory);
        return Task.FromResult(Created(project, reason));
    }

    private static string? TryGetReason(DirectoryInfo workingDirectory)
    {
        // Maven manifest: strongest and most common signal for Java Functions.
        if (File.Exists(Path.Combine(workingDirectory.FullName, "pom.xml")))
        {
            return "found pom.xml";
        }

        // Gradle build scripts (Groovy or Kotlin DSL).
        string? gradle = FirstExisting(workingDirectory, "build.gradle", "build.gradle.kts");
        if (gradle is not null)
        {
            return $"found {gradle}";
        }

        // Fallback: a conventional Java source tree.
        if (Directory.Exists(Path.Combine(workingDirectory.FullName, "src", "main", "java")))
        {
            return "found src/main/java";
        }

        return null;
    }

    private static string? FirstExisting(DirectoryInfo directory, params string[] fileNames)
    {
        foreach (string name in fileNames)
        {
            if (File.Exists(Path.Combine(directory.FullName, name)))
            {
                return name;
            }
        }

        return null;
    }
}
