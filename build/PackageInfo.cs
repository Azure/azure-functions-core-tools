using System;
using System.Collections.Generic;
using System.Text;

namespace Build
{
    internal class PackageInfo
    {
        public PackageInfo(string Name, string Version)
        {
            this.Name = Name;
            this.Version = Version;
        }

        public string Name { get; set; }
        public string Version { get; set; }
    }
}
