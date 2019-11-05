using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers.Commons
{
    public class ListKeysResponse
    {
        public List<StorageAccountKey> keys { get; set; }
    }
}
