using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Telemetry;
using Colors.Net;
using Fclp.Internals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Azure.Functions.Cli.Helpers
{
    internal static class TelemetryHelpers
    {
        public static IEnumerable<string> GetCommandsFromCommandLineOptions(IEnumerable<ICommandLineOption> options)
        {
            return options.Select(option => option.HasLongName ? option.LongName : option.ShortName);
        }

        public static void AddCommandEventToDictionary(IDictionary<string, string> events, string eventName, string eventVal)
        {
            if (StaticSettings.IsTelemetryEnabled)
            {
                try
                {
                    events[eventName] = eventVal;
                }
                catch (Exception ex)
                {
                    if (StaticSettings.IsDebug)
                    {
                        ColoredConsole.Error.WriteLine(ex.ToString());
                    }
                }
            }
        }

        public static void UpdateTelemetryEvent(TelemetryEvent telemetryEvent, IDictionary<string, string> commandEvents)
        {
            try
            {
                var languageContext = GlobalCoreToolsSettings.CurrentLanguageOrNull ?? "N/A";
                telemetryEvent.GlobalSettings["language"] = languageContext;
                telemetryEvent.CommandEvents = commandEvents;
            }
            catch (Exception ex)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.Error.WriteLine(ex.ToString());
                }
            }
        }

        public static void LogEventIfAllowedSafe(ITelemetry telemetry, TelemetryEvent telemetryEvent)
        {
            try
            {
                LogEventIfAllowed(telemetry, telemetryEvent);
            }
            catch
            { 
                // oh well!
            }
        }

        public static void LogEventIfAllowed(ITelemetry telemetry, TelemetryEvent telemetryEvent)
        {
            if (!telemetry.Enabled)
            {
                return;
            }

            telemetryEvent.Parameters = telemetryEvent.Parameters ?? new List<string>();
            var properties = new Dictionary<string, string>
            {
                { "commandName" , telemetryEvent.CommandName },
                { "iActionName" , telemetryEvent.IActionName },
                { "parameters" , string.Join(",", telemetryEvent.Parameters) },
                { "prefixOrScriptRoot" , telemetryEvent.PrefixOrScriptRoot.ToString() },
                { "parseError" , telemetryEvent.ParseError.ToString() },
                { "isSuccessful" , telemetryEvent.IsSuccessful.ToString() }
            };

            if (telemetryEvent.CommandEvents != null)
            {
                foreach (KeyValuePair<string, string> keyValue in telemetryEvent.CommandEvents)
                {
                    properties[keyValue.Key] = keyValue.Value;
                }
            }

            if (telemetryEvent.GlobalSettings != null)
            {
                foreach (KeyValuePair<string, string> keyValue in telemetryEvent.GlobalSettings)
                {
                    properties[$"global_{keyValue.Key}"] = keyValue.Value;
                }
            }

            var measurements = new Dictionary<string, double>
            {
                { "timeTaken" , telemetryEvent.TimeTaken }
            };

            telemetry.TrackEvent(telemetryEvent.CommandName, properties, measurements);
            telemetry.Flush();
        }
    }
}
