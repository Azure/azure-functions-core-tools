// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Preserves an existing Python app's bytes while applying one hash-bound function-body append.
/// </summary>
internal sealed class PythonTemplateAppendExecutor : IPythonTemplateAppendExecutor
{
    public async Task AppendAsync(string absoluteTargetPath, string generatedSnippet, string expectedTargetSha256, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteTargetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedSnippet);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTargetSha256);
        if (!Path.IsPathFullyQualified(absoluteTargetPath))
        {
            throw new ArgumentException("The approved Python app path must be absolute.", nameof(absoluteTargetPath));
        }

        if (expectedTargetSha256.Length != 64 || expectedTargetSha256.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("The approved Python app digest must be SHA-256 hex.", nameof(expectedTargetSha256));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using FileStream stream = new(absoluteTargetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken);
        if (!string.Equals(Convert.ToHexString(digest), expectedTargetSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Python app changed after preview. Preview the function again before appending it.");
        }

        bool needsSeparator = false;
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            needsSeparator = stream.ReadByte() != '\n';
        }

        byte[] content = Encoding.UTF8.GetBytes(needsSeparator ? Environment.NewLine + generatedSnippet : generatedSnippet);
        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0, SeekOrigin.End);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
