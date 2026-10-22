// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Binds a generated primary output to the canonical stack and language to persist for its project root.
/// </summary>
internal sealed record TemplateProjectConfigurationPlan(string PrimaryOutput, string Stack, string Language)
{
    public string Root => Path.GetDirectoryName(PrimaryOutput)!;

    public string ConfigurationPath => CliConfigurationPathsOptions.GetProjectConfigPath(new(Root));
}

/// <summary>
/// Retains the canonical bytes submitted by a successful create-only configuration write.
/// </summary>
internal sealed class TemplateProjectConfigurationWriteReceipt
{
    private readonly byte[] _content;

    internal TemplateProjectConfigurationWriteReceipt(string configurationPath, ReadOnlyMemory<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ConfigurationPath = configurationPath;
        _content = content.ToArray();
    }

    public string ConfigurationPath { get; }
    public ReadOnlyMemory<byte> Content => _content.ToArray();
}

/// <summary>
/// Creates CLI-owned configuration without replacing user-owned project configuration.
/// </summary>
internal sealed class TemplateProjectConfigurationWriter(IFileSystem fileSystem)
{
    internal const string ExistingConfigurationDiagnostic =
        "Existing project configuration is user-owned. Use the existing initializer to adopt or repair it.";

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <summary>
    /// Checks whether the configuration destination is already occupied by a file or directory.
    /// </summary>
    /// <exception cref="ArgumentNullException">The plan is null.</exception>
    public bool ConfigurationExists(TemplateProjectConfigurationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return _fileSystem.FileExists(plan.ConfigurationPath) || _fileSystem.DirectoryExists(plan.ConfigurationPath);
    }

    /// <summary>
    /// Atomically creates configuration for an existing generated primary output, refusing an occupied destination.
    /// </summary>
    /// <exception cref="ArgumentNullException">The plan or one of its required fields is null.</exception>
    /// <exception cref="ArgumentException">A required field is blank or the primary output is not an absolute path.</exception>
    /// <exception cref="FileNotFoundException">The generated primary output no longer exists.</exception>
    /// <exception cref="InvalidOperationException">The configuration destination is already occupied.</exception>
    /// <exception cref="IOException">The destination became occupied during creation or the atomic write failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The configuration destination cannot be written.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before configuration was committed.</exception>
    public Task<TemplateProjectConfigurationWriteReceipt> WriteAsync(TemplateProjectConfigurationPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.PrimaryOutput);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.Stack);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.Language);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(plan.PrimaryOutput))
        {
            throw new ArgumentException("The configuration plan must reference an absolute primary output.", nameof(plan));
        }

        if (!_fileSystem.FileExists(plan.PrimaryOutput))
        {
            throw new FileNotFoundException("The generated primary output is missing. Project configuration was not written.", plan.PrimaryOutput);
        }

        if (ConfigurationExists(plan))
        {
            throw new InvalidOperationException(ExistingConfigurationDiagnostic);
        }

        Dictionary<string, string> stack = new(StringComparer.Ordinal)
        {
            [CliConfigurationNames.StackRuntimeKey] = plan.Stack,
            [CliConfigurationNames.StackLanguageKey] = plan.Language,
        };
        Dictionary<string, object> payload = new(StringComparer.Ordinal) { [CliConfigurationNames.StackSectionName] = stack };
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        TemplateProjectConfigurationWriteReceipt receipt = new(plan.ConfigurationPath, Encoding.UTF8.GetBytes(json));
        return WriteReceiptAsync();

        async Task<TemplateProjectConfigurationWriteReceipt> WriteReceiptAsync()
        {
            await _fileSystem.CreateAllTextAsync(plan.ConfigurationPath, json, cancellationToken);
            return receipt;
        }
    }
}