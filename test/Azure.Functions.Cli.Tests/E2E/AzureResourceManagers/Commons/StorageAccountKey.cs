using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons
{
    public class StorageAccountKey
    {
        public string keyName { get; set; }
        public string value { get; set; }
        public string permissions { get; set; }
    }
}
