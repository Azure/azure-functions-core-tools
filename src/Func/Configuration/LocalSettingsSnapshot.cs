// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Configuration;

internal sealed class LocalSettingsSnapshot
{
    public static LocalSettingsSnapshot Empty { get; } = new();

    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public LocalSettingsHostSnapshot? Host { get; init; }

    public string? WorkerRuntime =>
        Values.TryGetValue(CliConfigurationNames.WorkerRuntimeSettingName, out string? value)
            && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
}
