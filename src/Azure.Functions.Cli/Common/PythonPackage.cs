using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Common
{
    public class PythonPackage
    {
        // azure-functions-worker
        public string Name { get; set; }

        // >=1.0.0,<1.0.3
        public string Specification { get; set; }

        // python_version < '2.8' or python_version == '2.7'
        public string EnvironmentMarkers { get; set; }

        // @ file:///somewhere
        public string DirectReference { get; set; }
    }
}
