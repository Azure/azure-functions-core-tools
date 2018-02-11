using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Arm
{
    internal interface IArmManager
    {
        Task<IEnumerable<Site>> GetFunctionAppsAsync();
        Task<IEnumerable<Site>> GetFunctionAppsAsync(Subscription subscription);
        Task<Site> GetFunctionAppAsync(string name);
        Task<ArmWebsitePublishingCredentials> GetUserAsync();
        Task UpdateUserAsync(string userName, string password);
        Task<Site> EnsureScmTypeAsync(Site functionApp);
        Task<IEnumerable<TenantSubscriptionMap>> GetTenants();
        Task<IEnumerable<Subscription>> GetSubscriptionsAsync();
        Task<Subscription> GetCurrentSubscriptionAsync();
        Task<Site> LoadSitePublishingCredentialsAsync(Site site);
        Task<IEnumerable<StorageAccount>> GetStorageAccountsAsync();
        Task<StorageAccount> LoadAsync(StorageAccount storageAccount);
        Task<IEnumerable<StorageAccount>> GetStorageAccountsAsync(Subscription subscription);
        Task<StorageAccount> GetStorageAccountsAsync(ArmWrapper<object> armWrapper);
        Task<Dictionary<string, string>> GetFunctionAppAppSettings(Site functionApp);
        Task<HttpResult<Dictionary<string, string>, string>> UpdateFunctionAppAppSettings(Site functionApp, IDictionary<string, string> appSettings);
        Task<Dictionary<string, AppServiceConnectionString>> GetFunctionAppConnectionStrings(Site functionApp);
    }
}
