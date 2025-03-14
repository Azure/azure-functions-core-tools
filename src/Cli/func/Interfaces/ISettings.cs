using System.Collections.Generic;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface ISettings
    {
        bool DisplayLaunchingRunServerWarning { get; set; }

        bool RunFirstTimeCliExperience { get; set; }

        string CurrentSubscription { get; set; }

        string CurrentTenant { get; set; }

        string MachineId { get; set; }

        string IsDockerContainer { get; set; }

        Dictionary<string, object> GetSettings();

        void SetSetting(string name, string value);
    }
}
