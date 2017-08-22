using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.IO;
using Azure.Functions.Cli.Common;
using System;

namespace Azure.Functions.Cli.Arm
{
    class ArmTokenCache : TokenCache
    {
        public string CacheFilePath;
        private static readonly object FileLock = new object();
        private const string reason = "token.cache.1";

        public ArmTokenCache()
        {
            CacheFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions", "tokenCache.dat");
            this.AfterAccess = AfterAccessNotification;
            this.BeforeAccess = BeforeAccessNotification;
            lock (FileLock)
            {
                var content = File.Exists(CacheFilePath)
                    ? ProtectedData.Unprotect(File.ReadAllBytes(CacheFilePath), reason)
                    : null;
                this.Deserialize(content);
            }
        }

        // Empties the persistent store.
        public override void Clear()
        {
            base.Clear();
            File.Delete(CacheFilePath);
        }

        // Triggered right before ADAL needs to access the cache.
        // Reload the cache from the persistent store in case it changed since the last access.
        void BeforeAccessNotification(TokenCacheNotificationArgs args)
        {
            lock (FileLock)
            {
                var content = File.Exists(CacheFilePath)
                    ? ProtectedData.Unprotect(File.ReadAllBytes(CacheFilePath), reason)
                    : null;
                this.Deserialize(content);
            }
        }

        // Triggered right after ADAL accessed the cache.
        void AfterAccessNotification(TokenCacheNotificationArgs args)
        {
            // if the access operation resulted in a cache update
            if (this.HasStateChanged)
            {
                lock (FileLock)
                {
                    // reflect changes in the persistent store
                    var encrypted = ProtectedData.Protect(this.Serialize(), reason);
                    File.WriteAllBytes(CacheFilePath, encrypted);
                    // once the write operation took place, restore the HasStateChanged bit to false
                    this.HasStateChanged = false;
                }
            }
        }
    }
}
