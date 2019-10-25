using System;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Azure.Functions.Cli.Helpers
{
    public static class StorageBlobHelper
    {
        public static async Task<string> PrepareScmRunFromPackageBlob(string connectionString, string containerName, string blobName, int expiryInDays)
        {
            containerName = containerName.ToLower();

            var storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            var container = blobClient.GetContainerReference(containerName);
            await container.CreateIfNotExistsAsync();
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobName);

            SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy()
            {
                SharedAccessExpiryTime = DateTimeOffset.Now.AddDays(expiryInDays),
                Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.Read
            };

            var blobSas = blockBlob.GetSharedAccessSignature(sasConstraints);
            return blockBlob.Uri + blobSas;
        }
    }
}
