// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Colors.Net;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DotNet.PlatformAbstractions;

namespace Azure.Functions.Cli.Telemetry
{
    // Most of the Telemetry implementation is inspired / acquired from https://github.com/dotnet/cli/tree/master/src/dotnet/Telemetry
    public class Telemetry : ITelemetry
    {
        private static string _currentSessionId = null;
        private readonly int _senderCount;
        private TelemetryClient _client = null;
        private Dictionary<string, string> _commonProperties = null;
        private Dictionary<string, double> _commonMeasurements = null;
        private Task _trackEventTask = null;

        public Telemetry(
            string sessionId,
            bool blockThreadInitialization = false,
            int senderCount = 3)
        {
            if (!StaticSettings.IsTelemetryEnabled)
            {
                Enabled = false;
                return;
            }

            Enabled = true;

            // Store the session ID in a static field so that it can be reused
            _currentSessionId = sessionId ?? Guid.NewGuid().ToString();
            _senderCount = senderCount;
            if (blockThreadInitialization)
            {
                InitializeTelemetry();
            }
            else
            {
                // initialize in task to offload to parallel thread
                _trackEventTask = Task.Run(() => InitializeTelemetry());
            }
        }

        public bool Enabled { get; }

        public void TrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements)
        {
            if (!Enabled)
            {
                return;
            }

            // continue the task in different threads
            _trackEventTask = _trackEventTask.ContinueWith(
                x => TrackEventTask(eventName, properties, measurements));
        }

        public void Flush()
        {
            if (!Enabled || _trackEventTask == null)
            {
                return;
            }

            _trackEventTask.Wait();
        }

        public void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements)
        {
            if (!Enabled)
            {
                return;
            }

            TrackEventTask(eventName, properties, measurements);
        }

        private void InitializeTelemetry()
        {
            try
            {
                var persistenceChannel = new PersistenceChannel.PersistenceChannel(sendersCount: _senderCount);
                persistenceChannel.SendingInterval = TimeSpan.FromMilliseconds(1);
                var telemetryConfiguration = new TelemetryConfiguration
                {
                    TelemetryChannel = persistenceChannel,
                    ConnectionString = $"InstrumentationKey={Constants.TelemetryInstrumentationKey}"
                };
                _client = new TelemetryClient(telemetryConfiguration);
                _client.Context.Session.Id = _currentSessionId;
                _client.Context.Device.OperatingSystem = RuntimeEnvironment.OperatingSystem;

                // We don't want to log this.
                // Setting it to null doesn't work. So might as well log the session id.
                _client.Context.Cloud.RoleInstance = $"private-{_currentSessionId}";

                _commonProperties = new TelemetryCommonProperties().GetTelemetryCommonProperties();
                _commonMeasurements = new Dictionary<string, double>();
            }
            catch (Exception e)
            {
                _client = null;

                // we dont want to fail the tool if telemetry fails.
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.Error.WriteLine(e.ToString());
                }
            }
        }

        private void TrackEventTask(
            string eventName,
            IDictionary<string, string> properties,
            IDictionary<string, double> measurements)
        {
            if (_client == null)
            {
                return;
            }

            try
            {
                Dictionary<string, string> eventProperties = GetEventProperties(properties);
                Dictionary<string, double> eventMeasurements = GetEventMeasures(measurements);

                eventProperties.Add("event id", Guid.NewGuid().ToString());

                _client.TrackEvent(PrependProducerNamespace(eventName), eventProperties, eventMeasurements);
            }
            catch (Exception e)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.Error.WriteLine(e.ToString());
                }
            }
        }

        private static string PrependProducerNamespace(string eventName)
        {
            return "core-tools: func " + eventName;
        }

        private Dictionary<string, double> GetEventMeasures(IDictionary<string, double> measurements)
        {
            Dictionary<string, double> eventMeasurements = new Dictionary<string, double>(_commonMeasurements);
            if (measurements != null)
            {
                foreach (KeyValuePair<string, double> measurement in measurements)
                {
                    eventMeasurements[measurement.Key] = measurement.Value;
                }
            }

            return eventMeasurements;
        }

        private Dictionary<string, string> GetEventProperties(IDictionary<string, string> properties)
        {
            if (properties != null)
            {
                var eventProperties = new Dictionary<string, string>(_commonProperties);
                foreach (KeyValuePair<string, string> property in properties)
                {
                    eventProperties[property.Key] = property.Value;
                }

                return eventProperties;
            }
            else
            {
                return _commonProperties;
            }
        }
    }
}
