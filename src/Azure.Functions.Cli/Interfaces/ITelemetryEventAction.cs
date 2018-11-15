using System;
using System.Collections.Generic;
using System.Text;
using static Azure.Functions.Cli.Helpers.TelemetryHelpers;

namespace Azure.Functions.Cli.Interfaces
{
    interface ITelemetryEventAction
    {
        void UpdateTelemetryLogEvent(ConsoleAppLogEvent consoleEvent);
    }
}
