using System.Globalization;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class StorageAccount : BaseResource
    {
        private string _csmIdTemplate = "/subscriptions/{0}/resourceGroups/{1}/providers/Microsoft.Storage/storageAccounts/{2}";

        public string StorageAccountName { get; private set; }

        public override string ArmId
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, this._csmIdTemplate, this.SubscriptionId, this.ResourceGroupName, this.StorageAccountName);
            }
        }

        public string StorageAccountKey { get; set; }

        public string Location { get; set; }

        public StorageAccount(string subscriptionId, string resourceGroupName, string storageAccountName, string location)
            : base(subscriptionId, resourceGroupName)
        {
            StorageAccountName = storageAccountName;
            Location = location;
        }

        public string GetConnectionString()
        {
            return string.Format(Constants.StorageConnectionStringTemplate, StorageAccountName, StorageAccountKey);
        }
    }
}