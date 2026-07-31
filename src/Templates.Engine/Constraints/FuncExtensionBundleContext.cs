// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// The project's resolved extension bundle, supplied by the CLI so the custom
/// <c>func-extension-bundle</c> constraint can evaluate a template-declared
/// requirement against it.
/// </summary>
/// <param name="BundleId">The resolved extension bundle id.</param>
/// <param name="BundleVersion">The resolved extension bundle version.</param>
internal sealed record FuncExtensionBundleContext(string BundleId, string BundleVersion);
