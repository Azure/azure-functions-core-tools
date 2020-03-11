using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Common
{
    public enum DeployStatus
    {
        Unknown = -1,
        Pending = 0,
        Building = 1,
        Deploying = 2,
        Failed = 3,
        Success = 4
    }
}
