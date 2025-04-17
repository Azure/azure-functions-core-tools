// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Telemetry
{
    public interface ITelemetry
    {
        internal bool Enabled { get; }

        internal void TrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements);

        internal void Flush();
    }
}
