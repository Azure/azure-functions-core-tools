using System.Collections.Generic;

namespace Azure.Functions.Cli.Tests.E2ETests.AzureResourceManagers.Commons
{
    public class ListKeysResponse
    {
        public List<StorageAccountKey> keys { get; set; }
    }
}
