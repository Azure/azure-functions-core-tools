// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Projects;

namespace Azure.Functions.Cli.Templates.Java;

/// <summary>
/// Qualifies the existing Java project scaffold without adding a Maven operation to initialization.
/// </summary>
internal sealed class JavaProjectWorkflowProvider(IFileSystem fileSystem) : INativeProjectWorkflowProvider
{
    internal static readonly OwnedProjectPreparationDefinition Definition = new(
        "java", "Java", "Azure.Functions.Templates.Java.Basic", "Azure.Functions.Java.Basic", "provenance/java/project.json",
        ["content/project/.template.config/template.json", "content/project/.template.config/func.host.json",
            "content/project/host.json", "content/project/pom.xml", "content/project/src/main/java/com/function/Function.java",
            "content/project/local.settings.json", "content/project/.funcignore", "content/project/.gitignore"]);
    private readonly OwnedProjectPreparationSource _sources = new(fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)));

    public string Stack => "java";

    public bool Handles(TemplateCatalogEntry entry) => OwnedProjectPreparationSource.Handles(entry, Definition);

    public async Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeProjectWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await _sources.QualifyAsync(context.SelectedTemplate, Definition, cancellationToken);
            return NativeTemplateInvocationPreparation.Ready(context.Invocation);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeTemplateInvocationPreparation.Reject($"Cannot qualify the owned Java project source: {exception.Message}");
        }
    }
}
