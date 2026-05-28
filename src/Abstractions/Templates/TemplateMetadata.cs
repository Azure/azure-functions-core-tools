// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Per-template descriptor surfaced by an <see cref="ITemplateEngineProvider"/>.
/// Engine-agnostic: V2 and DotNet engines project their native schemas into
/// this shape before exposing them to the orchestrator.
/// </summary>
/// <param name="UserPrompts">
/// The per-template input declarations the orchestrator hydrates into
/// <c>Option&lt;T&gt;</c> instances at stage B of the two-stage parse.
/// Empty when the template has no user-configurable inputs beyond the
/// function name.
/// </param>
/// <param name="RequiresExtensionBundle">
/// When <c>true</c>, the template scaffolds a binding that needs an extension
/// bundle to be resolvable at host launch. Drives the bundle-presence gate
/// in the orchestrator. Always <c>false</c> for DotNet templates (which
/// declare bindings via worker-SDK package references, not bundles).
/// </param>
/// <param name="MinBundleVersion">
/// Optional minimum extension-bundle version this template is compatible with,
/// expressed as a NuGet version range (e.g. <c>"[4.18.0, )"</c>). Workload-wide
/// <c>minBundleVersion</c> in <c>content/templates-workload.json</c> remains the
/// authoritative gate; per-template overrides are a future extension.
/// <c>null</c> when no per-template override.
/// </param>
public sealed record TemplateMetadata(
    IReadOnlyList<TemplateUserPrompt> UserPrompts,
    bool RequiresExtensionBundle,
    string? MinBundleVersion);
