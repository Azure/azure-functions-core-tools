// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed record TemplateProjectConfigurationPlan(string PrimaryOutput, string Stack, string Language)
{
    public string Root => Path.GetDirectoryName(PrimaryOutput)!;

    public string ConfigurationPath => CliConfigurationPathsOptions.GetProjectConfigPath(new(Root));
}

internal sealed class TemplateProjectConfigurationWriter(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public Task WriteAsync(TemplateProjectConfigurationPlan plan, CancellationToken cancellationToken)
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

        if (_fileSystem.FileExists(plan.ConfigurationPath))
        {
            throw new InvalidOperationException("Existing project configuration is user-owned. Use the existing initializer to adopt or repair it.");
        }

        Dictionary<string, string> stack = new(StringComparer.Ordinal)
        {
            [CliConfigurationNames.StackRuntimeKey] = plan.Stack,
            [CliConfigurationNames.StackLanguageKey] = plan.Language,
        };
        Dictionary<string, object> payload = new(StringComparer.Ordinal) { [CliConfigurationNames.StackSectionName] = stack };
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        return _fileSystem.WriteAllTextAsync(plan.ConfigurationPath, json, cancellationToken);
    }
}