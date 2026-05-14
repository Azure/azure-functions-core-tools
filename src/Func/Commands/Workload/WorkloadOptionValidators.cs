// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// Shared parse-time validators for <c>func workload</c> options and
/// arguments. Centralized so message wording and parsing rules stay
/// consistent across install/uninstall/update/search.
/// </summary>
internal static class WorkloadOptionValidators
{
    /// <summary>
    /// Rejects an option value that is non-empty and is not a valid
    /// <see cref="NuGetVersion"/> string. Empty / null values pass (the
    /// option is optional).
    /// </summary>
    public static void AddSemVerValidator(this Option<string?> option)
    {
        ArgumentNullException.ThrowIfNull(option);

        option.Validators.Add(result =>
        {
            string? value = result.GetValue(option);
            if (!string.IsNullOrWhiteSpace(value) && !NuGetVersion.TryParse(value, out _))
            {
                result.AddError($"'{value}' is not a valid semver version.");
            }
        });
    }

    /// <summary>
    /// Rejects a required workload-id argument that is null or whitespace.
    /// </summary>
    public static void AddRequiredIdValidator(this Argument<string> argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        argument.Validators.Add(result =>
        {
            string? value = result.GetValue(argument);
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("A workload id is required.");
            }
        });
    }

    /// <summary>
    /// Rejects an optional workload-id argument that was supplied but is
    /// only whitespace. A genuinely omitted argument still passes.
    /// </summary>
    public static void AddOptionalIdValidator(this Argument<string?> argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        argument.Validators.Add(result =>
        {
            // An omitted optional argument parses to null and stays valid;
            // only reject explicit whitespace-only input.
            string? value = result.GetValue(argument);
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                result.AddError("A workload id is required.");
            }
        });
    }
}
