// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Parameters;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Adds a func-owned wrapper around template engine metadata.
/// </summary>
/// <remarks>
/// Wraps engine metadata and its parsed func host configuration.
/// </remarks>
/// <param name="template">The underlying template engine metadata.</param>
/// <param name="hostData">The parsed func host configuration.</param>
internal sealed class FuncTemplateInfo(ITemplateInfo template, FuncTemplateHostData hostData)
    : ITemplateInfo
{
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

        return new(template, settings.ReadHostData(template));
    }
}
