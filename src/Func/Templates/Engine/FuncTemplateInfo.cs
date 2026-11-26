// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Adds a func-owned wrapper around template engine metadata.
/// </summary>
/// <remarks>
/// Wraps engine metadata and its parsed func host configuration.
/// </remarks>
/// <param name="template">The underlying template engine metadata.</param>
/// <param name="hostData">The parsed func host configuration.</param>
/// <param name="settings">The engine environment that discovered the template, used to instantiate it.</param>
internal sealed class FuncTemplateInfo(ITemplateInfo template,
    FuncTemplateHostData hostData, IEngineEnvironmentSettings? settings = null)
    : ITemplateInfo
{
    private readonly IEngineEnvironmentSettings? _settings = settings;

    public ITemplateInfo Template { get; } = template ?? throw new ArgumentNullException(nameof(template));

    public FuncTemplateHostData HostData { get; } = hostData ?? throw new ArgumentNullException(nameof(hostData));

    public string? Author => Template.Author;

    public string? Description => Template.Description;

    public IReadOnlyList<string> Classifications => Template.Classifications;

    public string? DefaultName => Template.DefaultName;

    public string Identity => Template.Identity;

    public string? GroupIdentity => Template.GroupIdentity;

    public int Precedence => Template.Precedence;

    public string Name => Template.Name;

    public IReadOnlyDictionary<string, string> TagsCollection => Template.TagsCollection;

    public IParameterDefinitionSet ParameterDefinitions => Template.ParameterDefinitions;

    public string? ThirdPartyNotices => Template.ThirdPartyNotices;

    public IReadOnlyDictionary<string, IBaselineInfo> BaselineInfo => Template.BaselineInfo;

    public IReadOnlyList<string> ShortNameList => Template.ShortNameList;

    public IReadOnlyList<Guid> PostActions => Template.PostActions;

    public IReadOnlyList<TemplateConstraintInfo> Constraints => Template.Constraints;

    public bool PreferDefaultName => Template.PreferDefaultName;

    public Guid GeneratorId => Template.GeneratorId;

    public string MountPointUri => Template.MountPointUri;

    public string ConfigPlace => Template.ConfigPlace;

    public string? LocaleConfigPlace => Template.LocaleConfigPlace;

    public string? HostConfigPlace => Template.HostConfigPlace;

    // Keep obsolete engine members available so this wrapper remains substitutable for ITemplateInfo.
#pragma warning disable CS0618
    public string ShortName => Template.ShortName;

    public IReadOnlyDictionary<string, ICacheTag> Tags => Template.Tags;

    public IReadOnlyDictionary<string, ICacheParameter> CacheParameters => Template.CacheParameters;

    public IReadOnlyList<ITemplateParameter> Parameters => Template.Parameters;

    public bool HasScriptRunningPostActions
    {
        get => Template.HasScriptRunningPostActions;
        set => Template.HasScriptRunningPostActions = value;
    }
#pragma warning restore CS0618

    /// <summary>
    /// Loads the selected func host configuration and wraps the template metadata.
    /// </summary>
    /// <exception cref="JsonException">The host configuration contains invalid metadata.</exception>
    /// <exception cref="FileNotFoundException">The declared host configuration file is missing.</exception>
    /// <exception cref="IOException">The template source cannot be mounted or the host configuration cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the host configuration is denied.</exception>
    public static FuncTemplateInfo Create(ITemplateInfo template, IEngineEnvironmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(settings);

        return new(template, settings.ReadHostData(template), settings);
    }

    /// <summary>
    /// Instantiates this template into <paramref name="outputPath"/> through the template engine,
    /// which owns parameter conversion, defaults, validation, collision detection, and file creation.
    /// </summary>
    /// <param name="name">The requested output name, or <c>null</c> to use the output folder name.</param>
    /// <param name="outputPath">The directory the template creates content in.</param>
    /// <param name="parameters">Raw template parameter values keyed by parameter name.</param>
    /// <param name="force">Whether the engine may overwrite existing files.</param>
    /// <param name="dryRun">Whether to compute the creation effects without writing files.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// The template was not discovered through a template engine environment.
    /// </exception>
    public async Task<ITemplateCreationResult> InstantiateAsync(string? name, string outputPath,
        IReadOnlyDictionary<string, string> parameters, bool force, bool dryRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(parameters);
        if (_settings is null)
        {
            throw new InvalidOperationException(
                $"Template '{Identity}' cannot be instantiated without a template engine environment.");
        }

        string fallbackName = Path.GetFileName(Path.TrimEndingDirectorySeparator(outputPath));
        var inputParameters = parameters.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        TemplateCreator creator = new(_settings);
        return await creator.InstantiateAsync(
            Template,
            name,
            fallbackName,
            outputPath,
            inputParameters,
            forceCreation: force,
            dryRun: dryRun,
            cancellationToken: cancellationToken);
    }
}
