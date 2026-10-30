// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using OpenTelemetry;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Adds CLI dimensions to span properties so queries do not need a resource-metadata join.
/// </summary>
internal sealed class CliActivityEnrichmentProcessor(IReadOnlyList<KeyValuePair<string, object>> attributes) : BaseProcessor<Activity>
{
    private readonly IReadOnlyList<KeyValuePair<string, object>> _attributes =
        attributes ?? throw new ArgumentNullException(nameof(attributes));

    public override void OnStart(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        foreach ((string key, object value) in _attributes)
        {
            if (data.GetTagItem(key) is null)
            {
                data.SetTag(key, value);
            }
        }
    }
}
