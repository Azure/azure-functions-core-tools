// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Constraints;

/// <summary>
/// <c>Microsoft.TemplateEngine</c> constraint factory for the
/// <c>func-extension-bundle</c> constraint. Templates declare it to gate
/// themselves on the resolved extension bundle the target project uses. The
/// constraint <c>args</c> accept three shapes:
/// <list type="number">
/// <item>A bare version-range string, targeting the implicit stable channel,
/// e.g. <c>"args": "[4.0.0, 5.0.0)"</c>.</item>
/// <item>An object with an explicit <c>id</c> and a <c>version</c> range, e.g.
/// <c>"args": { "id": "preview", "version": "[4.0.0, 5.0.0)" }</c>.</item>
/// <item>An object with only a <c>version</c> range, targeting the implicit
/// stable channel, e.g. <c>"args": { "version": "[4.0.0, 5.0.0)" }</c>.</item>
/// </list>
/// The <c>id</c> may be either a full bundle id
/// (e.g. <c>Microsoft.Azure.Functions.ExtensionBundle.Preview</c>) or a short
/// channel label (<c>stable</c>, <c>preview</c>, <c>experimental</c>); both are
/// resolved to a <see cref="BundleChannel"/>.
/// The resolved bundle context is exposed to the engine as host params (see
/// <see cref="FuncTemplateEngineHostParameters"/>): the bundle id under
/// <see cref="FuncTemplateEngineHostParameters.BundleChannel"/> and the bundle
/// version under <see cref="FuncTemplateEngineHostParameters.Bundle"/>. The
/// constraint reads those values back and returns <c>Allowed</c> when the
/// declared (or implied stable) channel matches and the resolved version is
/// within the declared range, and <c>Restricted</c> otherwise.
/// </summary>
internal sealed class FuncExtensionBundleConstraintFactory : ITemplateConstraintFactory
{
    /// <summary>
    /// The constraint <c>type</c> discriminator templates use in
    /// <c>template.json</c> to select this constraint.
    /// </summary>
    public const string ConstraintType = "func-extension-bundle";

    private static readonly Guid _componentId = Guid.Parse("6f2f4d9b-3c8a-4d1e-9a7b-4b2f7c0a1d84");

    Guid IIdentifiedComponent.Id => _componentId;

    string ITemplateConstraintFactory.Type => ConstraintType;

    Task<ITemplateConstraint> ITemplateConstraintFactory.CreateTemplateConstraintAsync(
        IEngineEnvironmentSettings environmentSettings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ITemplateConstraint>(new FuncExtensionBundleConstraint(environmentSettings, this));
    }

    private sealed class FuncExtensionBundleConstraint(
        IEngineEnvironmentSettings environmentSettings, ITemplateConstraintFactory factory)
        : ITemplateConstraint
    {
        private const string IdProperty = "id";
        private const string VersionProperty = "version";
        private const BundleChannel DefaultChannel = BundleChannel.Stable;

        private readonly IEngineEnvironmentSettings _environmentSettings = environmentSettings;
        private readonly ITemplateConstraintFactory _factory = factory;

        public string Type => _factory.Type;

        public string DisplayName => "Extension bundle";

        public TemplateConstraintResult Evaluate(string? args)
        {
            if (!TryParseArgs(args, out BundleChannel channel, out VersionRange? versionRange, out string? configError))
            {
                return TemplateConstraintResult.CreateEvaluationFailure(
                    this,
                    configError!,
                    $"Fix the '{ConstraintType}' constraint configuration so it specifies a valid version range (as a string or a '{VersionProperty}' property) and, optionally, an '{IdProperty}' (a bundle id or one of '{BundleHelpers.StableLabel}', '{BundleHelpers.PreviewLabel}', '{BundleHelpers.ExperimentalLabel}').");
            }

            string channelLabel = channel.ToDisplayString();

            if (!_environmentSettings.Host.TryGetHostParamDefault(FuncTemplateEngineHostParameters.Bundle, out string? resolvedVersion)
                || string.IsNullOrWhiteSpace(resolvedVersion))
            {
                return TemplateConstraintResult.CreateRestricted(
                    this,
                    $"This template requires the '{channelLabel}' extension bundle {RangeText(versionRange!)}, but no extension bundle is resolved for the current project.");
            }

            _environmentSettings.Host.TryGetHostParamDefault(FuncTemplateEngineHostParameters.BundleChannel, out string? resolvedId);

            if (!BundleHelpers.TryResolveChannel(resolvedId ?? string.Empty, out BundleChannel resolvedChannel)
                || resolvedChannel != channel)
            {
                return TemplateConstraintResult.CreateRestricted(
                    this,
                    $"This template requires the '{channelLabel}' extension bundle, but the current project uses '{resolvedId ?? "(none)"}'.");
            }

            if (!NuGetVersion.TryParse(resolvedVersion, out NuGetVersion? resolvedNuGetVersion))
            {
                return TemplateConstraintResult.CreateRestricted(
                    this,
                    $"This template requires the '{channelLabel}' extension bundle {RangeText(versionRange!)}, but the resolved bundle version '{resolvedVersion}' is not a valid version.");
            }

            if (!versionRange!.Satisfies(resolvedNuGetVersion))
            {
                return TemplateConstraintResult.CreateRestricted(
                    this,
                    $"This template requires the '{channelLabel}' extension bundle {RangeText(versionRange)}, but the current project uses version '{resolvedVersion}'.");
            }

            return TemplateConstraintResult.CreateAllowed(this);
        }

        private static string RangeText(VersionRange versionRange) => versionRange.OriginalString ?? versionRange.ToString();

        private static bool TryParseArgs(string? args, out BundleChannel channel, out VersionRange? versionRange, out string? configError)
        {
            channel = DefaultChannel;
            versionRange = null;
            configError = null;

            if (string.IsNullOrWhiteSpace(args))
            {
                configError = $"The '{ConstraintType}' constraint requires configuration arguments, but none were provided.";
                return false;
            }

            JsonNode? argsNode;
            try
            {
                argsNode = JsonNode.Parse(args);
            }
            catch (JsonException)
            {
                // Not JSON: treat the raw args as a bare version-range string
                // targeting the implicit stable channel.
                return TryParseRange(args, out versionRange, out configError);
            }

            switch (argsNode)
            {
                case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                    // A JSON string, e.g. "[4.0.0, 5.0.0)": a bare version range
                    // targeting the implicit stable channel.
                    return TryParseRange(value.GetValue<string>(), out versionRange, out configError);

                case JsonObject argsObject:
                    return TryParseObjectArgs(argsObject, ref channel, out versionRange, out configError);

                default:
                    configError = $"The '{ConstraintType}' constraint arguments must be a version range string or an object with a '{VersionProperty}' property.";
                    return false;
            }
        }

        private static bool TryParseObjectArgs(JsonObject argsObject, ref BundleChannel channel, out VersionRange? versionRange, out string? configError)
        {
            versionRange = null;
            configError = null;

            if (argsObject.ContainsKey(IdProperty))
            {
                string? parsedId = GetStringValue(argsObject, IdProperty);
                if (string.IsNullOrWhiteSpace(parsedId))
                {
                    configError = $"The '{ConstraintType}' constraint '{IdProperty}' value must be a non-empty string.";
                    return false;
                }

                if (!BundleHelpers.TryResolveChannel(parsedId!, out channel))
                {
                    configError = $"The '{ConstraintType}' constraint '{IdProperty}' value '{parsedId}' is not a recognized bundle id or channel label ('{BundleHelpers.StableLabel}', '{BundleHelpers.PreviewLabel}', '{BundleHelpers.ExperimentalLabel}').";
                    return false;
                }
            }

            string? rangeText = GetStringValue(argsObject, VersionProperty);
            if (string.IsNullOrWhiteSpace(rangeText))
            {
                configError = $"The '{ConstraintType}' constraint arguments are missing the required '{VersionProperty}' property.";
                return false;
            }

            return TryParseRange(rangeText, out versionRange, out configError);
        }

        private static bool TryParseRange(string? rangeText, out VersionRange? versionRange, out string? configError)
        {
            versionRange = null;
            configError = null;

            if (string.IsNullOrWhiteSpace(rangeText))
            {
                configError = $"The '{ConstraintType}' constraint requires a non-empty version range.";
                return false;
            }

            if (!VersionRange.TryParse(rangeText, out VersionRange? parsedRange))
            {
                configError = $"The '{ConstraintType}' constraint version range '{rangeText}' is not a valid version range.";
                return false;
            }

            versionRange = parsedRange;
            return true;
        }

        private static string? GetStringValue(JsonObject obj, string propertyName)
        {
            if (obj.TryGetPropertyValue(propertyName, out JsonNode? node)
                && node is JsonValue value
                && value.GetValueKind() == JsonValueKind.String)
            {
                return value.GetValue<string>();
            }

            return null;
        }
    }
}
