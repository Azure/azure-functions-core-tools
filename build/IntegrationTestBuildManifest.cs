using System;
using System.Collections.Generic;
using System.Text;

namespace Build
{
    internal class IntegrationTestBuildManifest
    {
        public string Build { get; set; }
        public Dictionary<string, string> Packages { get; set; }
        public string CoreToolsVersion { get; set; }
        public string CommitId { get; set; }
    }
}
