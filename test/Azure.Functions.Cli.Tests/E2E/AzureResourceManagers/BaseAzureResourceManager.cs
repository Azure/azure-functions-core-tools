using Azure.Functions.Cli.Common;
using System;

namespace Azure.Functions.Cli.Tests.E2E.AzureResourceManagers
{
    public abstract class BaseAzureResourceManager: IDisposable
    {
        private static string _accessToken;
        private static string _subscriptionId;
        private static string _windowsResourceGroup;
        private static string _linuxResourceGroup;

        public static string ManagementURL => Constants.DefaultManagementURL;

        protected static string AccessToken
        {
            get
            {
                if (_accessToken == null)
                {
                    _accessToken = Environment.GetEnvironmentVariable(Constants.AzureManagementAccessToken);
                    if (string.IsNullOrEmpty(_accessToken))
                    {
                        throw new Exception($"{Constants.AzureManagementAccessToken} is not defined in current environment");
                    }
                }
                return _accessToken;
            }
        }

        protected static string SubscriptionId
        {
            get
            {
                if (_subscriptionId == null)
                {
                    _subscriptionId = Environment.GetEnvironmentVariable(E2ETestConstants.TestSubscriptionId);
                    if (string.IsNullOrEmpty(_subscriptionId))
                    {
                        throw new Exception($"{E2ETestConstants.TestSubscriptionId} is not defined in current environment");
                    }
                }
                return _subscriptionId;
            }
        }

        protected static string WindowsResourceGroupName
        {
            get
            {
                if (_windowsResourceGroup == null)
                {
                    _windowsResourceGroup = Environment.GetEnvironmentVariable(E2ETestConstants.TestResourceGroupNameWindows);
                    if (string.IsNullOrEmpty(_windowsResourceGroup))
                    {
                        throw new Exception($"{E2ETestConstants.TestResourceGroupNameWindows} is not defined in current environment");
                    }
                }
                return _windowsResourceGroup;
            }
        }

        protected static string LinuxResourceGroupName
        {
            get
            {
                if (_linuxResourceGroup == null)
                {
                    _linuxResourceGroup = Environment.GetEnvironmentVariable(E2ETestConstants.TestResourceGroupNameLinux);
                    if (string.IsNullOrEmpty(_linuxResourceGroup))
                    {
                        throw new Exception($"{E2ETestConstants.TestResourceGroupNameLinux} is not defined in current environment");
                    }
                }
                return _linuxResourceGroup;
            }
        }

        public void Dispose()
        {
            try
            {
                CleanUp();
            }
            catch
            {
                // ITestOutputHelper cannot be used by fixture
            }
        }

        protected virtual void CleanUp()
        {
            throw new NotImplementedException();
        }
    }
}
