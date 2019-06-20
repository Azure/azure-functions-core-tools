using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Common
{
    [Flags]
    public enum BuildOption
    {
        None,
        Local,
        Remote,
        Container
    }
}
