// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Common
{
    internal class PersistentSettings : ISettings
    {
        private static readonly string PersistentSettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions", "config");

        private readonly DiskBacked<Dictionary<string, object>> _store;

        public PersistentSettings()
            : this(true)
        {
        }

        public PersistentSettings(bool global)
        {
            if (global)
            {
                FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(PersistentSettingsPath));
                _store = DiskBacked.Create<Dictionary<string, object>>(PersistentSettingsPath);
            }
            else
            {
                _store = DiskBacked.Create<Dictionary<string, object>>(Path.Combine(Environment.CurrentDirectory, ".config"));
            }
        }

        public bool DisplayLaunchingRunServerWarning
        {
            get { return GetConfig(true); } set { SetConfig(value); }
        }

        public bool RunFirstTimeCliExperience
        {
            get { return GetConfig(true); } set { SetConfig(value); }
        }

        public string CurrentSubscription
        {
            get { return GetConfig(string.Empty); } set { SetConfig(value); }
        }

        public string CurrentTenant
        {
            get { return GetConfig(string.Empty); } set { SetConfig(value); }
        }

        public string MachineId
        {
            get { return GetConfig(string.Empty);  } set { SetConfig(value); }
        }

        public string IsDockerContainer
        {
            get { return GetConfig(string.Empty); } set { SetConfig(value); }
        }

        private T GetConfig<T>(T @default = default(T), [CallerMemberName] string key = null)
        {
            if (_store.Value.ContainsKey(key))
            {
                return (T)_store.Value[key];
            }
            else
            {
                return @default;
            }
        }

        private void SetConfig(object value, [CallerMemberName] string key = null)
        {
            _store.Value[key] = value;
            _store.Commit();
        }

        public Dictionary<string, object> GetSettings()
        {
            return typeof(ISettings)
                .GetProperties()
                .ToDictionary(p => p.Name, p => p.GetValue(this));
        }

        public void SetSetting(string name, string value)
        {
            _store.Value[name] = JsonConvert.DeserializeObject<JToken>(value);
            _store.Commit();
        }
    }
}
