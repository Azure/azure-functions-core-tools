using Azure.Functions.Cli.Common;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Arm
{
    internal static class ArmClientExtensions
    {
        private static readonly JsonSerializerOptions s_serializerOptions = new(JsonSerializerDefaults.Web);

        public static async Task<InternalStorageAccountData> FindStorageAccount(this ResourceManager.ArmClient armClient, string accountName)
        {
            var resources = await armClient.GetTenants().First().GetResourcesAsync(
                new ResourceQueryContent(
                    $"where ['type'] =~ 'microsoft.storage/storageaccounts' and name =~ '{accountName}'"));

            if (resources.Value.Count != 1)
            {
                throw new CliException($"Unable to locate a storage account named {accountName}");
            }

            var account = resources.Value.Data.ToObjectFromJson<InternalStorageAccountData[]>(s_serializerOptions).First();

            await foreach (var key in armClient.GetStorageAccountResource(new(account.Id)).GetKeysAsync())
            {
                return account with { Key = key };
            }

            throw new InvalidOperationException("Unreachable");
        }

        public record InternalStorageAccountData(string Id, InternalStorageProperties Properties, StorageAccountKey Key);
        public record InternalStorageProperties(InternalStorageAccountEndpoints PrimaryEndpoints);
        public record InternalStorageAccountEndpoints(Uri Blob);
    }
}
