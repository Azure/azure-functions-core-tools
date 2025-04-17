// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Telemetry
{
    internal class TelemetryEvent
    {
        public TelemetryEvent()
        {
            Parameters = new List<string>();
            CommandEvents = new Dictionary<string, string>();
            GlobalSettings = new Dictionary<string, string>();
        }

        public string CommandName { get; set; }

        public string IActionName { get; set; }

        public IEnumerable<string> Parameters { get; set; }

        public bool PrefixOrScriptRoot { get; set; }

        public bool IsSuccessful { get; set; }

        public bool ParseError { get; set; }

        public long TimeTaken { get; set; }

        public IDictionary<string, string> CommandEvents { get; set; }

        public IDictionary<string, string> GlobalSettings { get; set; }
    }
}
