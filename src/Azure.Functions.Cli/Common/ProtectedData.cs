using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Common
{
    public class ProtectedData
    {
        private static ServiceProvider _services;
        static ProtectedData()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddDataProtection();
            _services = serviceCollection.BuildServiceProvider();
        }

        public static byte[] Protect(byte[] data, string purpose)
        {
            var protector = _services.GetDataProtector(purpose);
            return protector.Protect(data);
        }

        public static byte[] Unprotect(byte[] data, string purpose)
        {
            var protector = _services.GetDataProtector(purpose);
            return protector.Unprotect(data);
        }
    }
}