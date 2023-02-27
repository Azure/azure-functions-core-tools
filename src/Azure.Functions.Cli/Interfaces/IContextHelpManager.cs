using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Interfaces
{
    public interface IContextHelpManager
    {
        public Task<string> GetTriggerHelp(string triggerName, string language);
    }
}
