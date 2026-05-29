// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Closed discriminated union of named failure modes the
/// <c>func new</c> pipeline can surface. Wrapped in
/// <see cref="TemplateApplicationResult.Failed"/> when a provider or
/// orchestration step rejects the invocation. Each case carries the
/// structured data needed to render a hint without re-deriving anything.
/// </summary>
public abstract record TemplateApplicationFailure
{
    private TemplateApplicationFailure()
    {
    }

    /// <summary>
    /// No installed templates content workload matches the project's bundle
    /// channel (Node / Python only).
    /// </summary>
    public sealed record NoTemplatesWorkloadForChannel(
        string Stack,
        string Channel,
        string SuggestedPackageId,
        string SuggestedVersion)
        : TemplateApplicationFailure;

    /// <summary>
    /// The project requires an extension bundle but the bundles workload is
    /// not installed (or its resolution is empty). Pipeline step 11a.
    /// </summary>
    public sealed record MissingExtensionBundle(
        string Stack,
        string SuggestedBundleId)
        : TemplateApplicationFailure;

    /// <summary>
    /// The project's resolved bundle version is older than the selected
    /// templates workload's declared <c>minBundleVersion</c>. Pipeline step
    /// 11b. Hard error in v1 — no warning fallback.
    /// </summary>
    public sealed record MinBundleVersionTooOld(
        string InstalledBundleVersion,
        string RequiredRange,
        string TemplatesWorkloadVersion)
        : TemplateApplicationFailure;

    /// <summary>
    /// A file write failed during scaffold. Surfaces the offending path and
    /// the underlying I/O error message for diagnostics.
    /// </summary>
    public sealed record WriteFailed(string Path, string Message)
        : TemplateApplicationFailure;

    /// <summary>
    /// A user prompt was malformed or could not be hydrated into a valid
    /// option (e.g. invalid regex, unknown data type). Surfaces from the
    /// hydrator or from engine-level prompt evaluation.
    /// </summary>
    public sealed record InvalidPrompt(string PromptId, string Reason)
        : TemplateApplicationFailure;

    /// <summary>
    /// Any other engine- or provider-side error. Carries the optional inner
    /// exception so the renderer can decide whether to show a stack trace
    /// under <c>--verbose</c>.
    /// </summary>
    public sealed record ProviderError(string Message, Exception? InnerException)
        : TemplateApplicationFailure;
}
