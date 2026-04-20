// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Represents a Functions CLI workload — a modular extension that brings
/// language-specific or feature-specific capabilities to the CLI.
///
/// Language workloads (dotnet, python, node) typically implement
/// GetProjectInitializer, GetTemplateProviders, and GetPackProvider to
/// extend func init, func new, and func pack.
///
/// Feature workloads (durable, kubernetes) typically implement
/// RegisterCommands to add entirely new command trees (e.g., func durable).
/// They may also contribute templates to func new.
///
/// Workloads are distributed as NuGet packages and loaded at startup.
/// </summary>
public interface IWorkload
{
    /// <summary>
    /// Unique identifier for this workload (e.g., "dotnet", "python", "durable").
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Human-readable name for display purposes.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Brief description of what this workload provides.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Registers any commands this workload provides into the CLI command tree.
    /// Called during CLI startup after the built-in commands are registered.
    /// Feature workloads use this to add new command trees (e.g., func durable &lt;command&gt;).
    /// </summary>
    /// <param name="rootCommand">The root func command to add subcommands to.</param>
    public void RegisterCommands(Command rootCommand) { }

    /// <summary>
    /// Returns template providers for 'func new', or empty if this workload
    /// does not provide templates.
    /// </summary>
    public IReadOnlyList<ITemplateProvider> GetTemplateProviders() => [];

    /// <summary>
    /// Returns a project initializer for 'func init', or null if this workload
    /// does not provide init support.
    /// </summary>
    public IProjectInitializer? GetProjectInitializer() => null;

    /// <summary>
    /// Returns a pack provider for 'func pack', or null if this workload
    /// does not provide pack support.
    /// </summary>
    public IPackProvider? GetPackProvider() => null;
}
