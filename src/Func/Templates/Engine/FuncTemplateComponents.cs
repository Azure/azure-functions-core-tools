// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Func-owned <c>Microsoft.TemplateEngine</c> component set. Custom
/// constraint factories that gate templates on resolved func project context
/// (exposed as host params via <see cref="FuncTemplateEngineHostParameters"/>)
/// are registered here so <see cref="Templater.LoadDefaultComponents"/> loads
/// them alongside the default RunnableProjects and Edge components.
/// <para>
/// The <c>func-extension-bundle</c> constraint factory is added to
/// <see cref="AllComponents"/> once implemented (change
/// <c>func-universal-template-engine</c>, group 2 — constraints). Until then
/// the set is empty and no func-specific constraint gating is applied.
/// </para>
/// </summary>
internal static class FuncTemplateComponents
{
    /// <summary>
    /// The func constraint factory component set registered on the host. Each
    /// tuple pairs the engine component interface with the component instance,
    /// matching the shape of the default RunnableProjects and Edge component
    /// lists consumed by <see cref="Templater.LoadDefaultComponents"/>.
    /// </summary>
    public static IReadOnlyList<(Type Type, IIdentifiedComponent Instance)> AllComponents { get; } =
        Array.Empty<(Type, IIdentifiedComponent)>();
}
