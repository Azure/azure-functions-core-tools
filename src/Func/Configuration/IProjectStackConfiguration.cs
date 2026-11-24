// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Configuration;

/// <summary>
/// Reads project identity without discovering workloads; writes only resolved stack and language descriptors.
/// </summary>
internal interface IProjectStackConfiguration
{
    public ProjectStackSettings Read(DirectoryInfo projectDirectory);

    public Task CreateAsync(DirectoryInfo projectDirectory, IProjectStack stack, LanguageDescriptor? language, CancellationToken cancellationToken);

    public Task CompleteLanguageAsync(DirectoryInfo projectDirectory, IProjectStack stack, LanguageDescriptor language, CancellationToken cancellationToken);
}

internal sealed record ProjectStackSettings(bool HasProjectConfig = false, string? StackIdentifier = null, string? Language = null, string? WorkerRuntime = null, bool IsAdoptable = false)
{
    public const string StackSource = "stack.runtime in .func/config.json";
    public const string LanguageSource = "stack.language in .func/config.json";
    public const string WorkerRuntimeSource = "FUNCTIONS_WORKER_RUNTIME in local.settings.json";
}

internal sealed class ProjectStackConfigurationException(string message, Exception? innerException = null) : Exception(message, innerException);
