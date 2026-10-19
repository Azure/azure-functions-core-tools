// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates.Python;

/// <summary>
/// Binds a declared existing-app selector to one filename and its qualified source mapping for an invocation.
/// </summary>
internal sealed record PythonAppendTargetSelection
{
    private readonly NativeItemAppendMapping _source;

    private PythonAppendTargetSelection(NativeItemAppendMapping source, string fileName)
    {
        _source = source;
        FileName = fileName;
    }

    public string FileName { get; }

    public bool IsAlternate => FileName != _source.AppendTargetFileName;

    public string CanonicalFileName => _source.CreateTargetFileName;

    public FileIdentity? ExpectedIdentity { get; private init; }

    public PythonAppendTargetSelection WithIdentity(FileIdentity identity)
        => identity.IsRegular && identity.LinkCount == 1 ? this with { ExpectedIdentity = identity }
            : throw new InvalidDataException("Select an independent regular Python app with exactly one hard link. Nothing was appended.");

    public static PythonAppendTargetSelection Capture(NativeItemAppendMapping mapping, IReadOnlyDictionary<string, string?> parameters)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(parameters);
        string? filename = mapping.SelectedAppendFileNameParameter is { } parameter && parameters.TryGetValue(parameter, out string? value)
            ? value : mapping.AppendTargetFileName;
        if (PythonAppFileName.GetDiagnostic(filename) is { } diagnostic)
        {
            throw new InvalidDataException($"The selected existing Python app filename is invalid. {diagnostic}");
        }
        if (filename![..^".py".Length].Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')))
        {
            throw new InvalidDataException("Select one existing '.py' app with an ASCII basename using letters, digits, underscores or hyphens.");
        }
        return new(mapping, filename!);
    }

    public NativeItemAppendMapping Bind(NativeItemAppendMapping current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current == _source ? current with { AppendTargetFileName = FileName }
            : throw new InvalidDataException("The selected Python append target no longer matches its qualified source mapping.");
    }
}
