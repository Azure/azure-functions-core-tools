using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Interfaces
{
    public interface IContextHelpManager
    {
        string GetTriggerHelp(string triggerName, string language);
        Task LoadTriggerHelp(string language, List<string> triggerNames);
        bool IsValidTriggerNameForHelp(string triggerName);
        bool IsValidTriggerTypeForHelp(string triggerName);
        string GetTriggerTypeFromTriggerNameForHelp(string triggerName);
    }
}