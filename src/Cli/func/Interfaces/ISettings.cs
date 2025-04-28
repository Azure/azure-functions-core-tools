// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Interfaces
{
    internal interface ISettings
    {
        internal bool DisplayLaunchingRunServerWarning { get; set; }

        internal bool RunFirstTimeCliExperience { get; set; }

        internal string CurrentSubscription { get; set; }

        internal string CurrentTenant { get; set; }

        internal string MachineId { get; set; }

        internal string IsDockerContainer { get; set; }

        internal Dictionary<string, object> GetSettings();

        internal void SetSetting(string name, string value);
    }
}
