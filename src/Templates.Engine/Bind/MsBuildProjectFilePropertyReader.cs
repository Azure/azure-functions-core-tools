// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Xml.Linq;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Reads MSBuild properties by parsing the project file's
/// <c>&lt;PropertyGroup&gt;</c> entries directly. It intentionally avoids
/// loading the MSBuild engine — the bind source only needs simple,
/// unconditioned properties such as <c>TargetFramework</c>.
/// </summary>
internal sealed class MsBuildProjectFilePropertyReader(IFuncTemplateFileSystem fileSystem) : IProjectFilePropertyReader
{
    private static readonly string[] _projectPatterns = ["*.csproj", "*.fsproj", "*.vbproj"];

    private readonly IFuncTemplateFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <inheritdoc />
    public string? TryReadProperty(string projectDirectory, string propertyName)
    {
        if (string.IsNullOrEmpty(projectDirectory) || string.IsNullOrEmpty(propertyName))
        {
            return null;
        }

        string? projectFile = _projectPatterns
            .SelectMany(pattern => _fileSystem.EnumerateFiles(projectDirectory, pattern))
            .FirstOrDefault();
        if (projectFile is null)
        {
            return null;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(_fileSystem.ReadAllText(projectFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }

        return document.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "PropertyGroup", StringComparison.Ordinal))
            .SelectMany(group => group.Elements())
            .Where(e => string.Equals(e.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));
    }
}
