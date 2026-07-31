// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Xml.Linq;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Runs the func-owned add-reference post action: a targeted, idempotent XML
/// edit that adds a <c>&lt;PackageReference&gt;</c> (or
/// <c>&lt;ProjectReference&gt;</c>) to the project's <c>.csproj</c>. Re-running
/// with the same reference is a no-op.
/// </summary>
internal sealed class AddReferencePostActionHandler(IFuncTemplateFileSystem fileSystem) : IFuncPostActionHandler
{
    private const string ReferenceTypeArg = "referenceType";
    private const string ReferenceArg = "reference";
    private const string VersionArg = "version";
    private const string ProjectFileExtensionsArg = "projectFileExtensions";

    private readonly IFuncTemplateFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <inheritdoc />
    public Guid ActionId => FuncPostActionIds.AddReference;

    /// <inheritdoc />
    public Task<FuncPostActionResult> ExecuteAsync(FuncPostActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Execute(context));
    }

    private FuncPostActionResult Execute(FuncPostActionContext context)
    {
        IPostAction action = context.PostAction;
        bool continueOnError = action.ContinueOnError;

        string? reference = GetArg(action, ReferenceArg);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new FuncPostActionResult.Failed(
                $"The add-reference post-action is missing its '{ReferenceArg}' argument.", continueOnError);
        }

        string referenceType = GetArg(action, ReferenceTypeArg) ?? "package";
        string? version = GetArg(action, VersionArg);
        string extensions = GetArg(action, ProjectFileExtensionsArg) ?? ".csproj";

        string? projectPath = FindProjectFile(context.ProjectDirectory, extensions);
        if (projectPath is null)
        {
            return new FuncPostActionResult.Failed(
                $"No project file matching '{extensions}' was found in '{context.ProjectDirectory}' to add reference '{reference}'.",
                continueOnError);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(_fileSystem.ReadAllText(projectPath), LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new FuncPostActionResult.Failed(
                $"Failed to read project file '{projectPath}'.", continueOnError, ex);
        }

        if (document.Root is null)
        {
            return new FuncPostActionResult.Failed(
                $"Project file '{projectPath}' has no root element.", continueOnError);
        }

        bool isProjectReference = string.Equals(referenceType, "project", StringComparison.OrdinalIgnoreCase);
        XNamespace ns = document.Root.Name.Namespace;
        XName elementName = ns + (isProjectReference ? "ProjectReference" : "PackageReference");

        bool alreadyPresent = document.Descendants(elementName).Any(e =>
            string.Equals((string?)e.Attribute("Include"), reference, StringComparison.OrdinalIgnoreCase));
        if (alreadyPresent)
        {
            return new FuncPostActionResult.Succeeded();
        }

        var referenceElement = new XElement(elementName, new XAttribute("Include", reference));
        if (!isProjectReference && !string.IsNullOrWhiteSpace(version))
        {
            referenceElement.Add(new XAttribute("Version", version));
        }

        XElement itemGroup = document.Descendants(ns + "ItemGroup").FirstOrDefault(g => g.Elements(elementName).Any())
            ?? AddItemGroup(document.Root, ns);
        itemGroup.Add(referenceElement);

        try
        {
            _fileSystem.WriteAllText(projectPath, document.ToString(SaveOptions.DisableFormatting));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FuncPostActionResult.Failed(
                $"Failed to write project file '{projectPath}'.", continueOnError, ex);
        }

        string modified = Path.GetRelativePath(context.ProjectDirectory, projectPath);
        return new FuncPostActionResult.Succeeded { ModifiedFiles = [modified] };
    }

    private static XElement AddItemGroup(XElement root, XNamespace ns)
    {
        var itemGroup = new XElement(ns + "ItemGroup");
        root.Add(itemGroup);
        return itemGroup;
    }

    private string? FindProjectFile(string projectDirectory, string extensions)
    {
        foreach (string extension in extensions.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string pattern = extension.StartsWith('.') ? $"*{extension}" : $"*.{extension}";
            IReadOnlyList<string> matches = _fileSystem.EnumerateFiles(projectDirectory, pattern);
            if (matches.Count > 0)
            {
                return matches[0];
            }
        }

        return null;
    }

    private static string? GetArg(IPostAction action, string key) =>
        action.Args.TryGetValue(key, out string? value) ? value : null;
}
