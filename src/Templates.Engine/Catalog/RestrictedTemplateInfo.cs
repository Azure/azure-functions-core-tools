// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// A template hidden from the selectable set by an unsatisfied constraint,
/// carried so an explicit <c>--template</c> request can be answered with the
/// reason and call-to-action instead of a bare "unknown template" error.
/// </summary>
/// <param name="Template">The projected template, restricted for the current project.</param>
/// <param name="Reason">Why the template is unavailable, including the call-to-action.</param>
internal sealed record RestrictedTemplateInfo(FunctionTemplateInfo Template, string Reason);
