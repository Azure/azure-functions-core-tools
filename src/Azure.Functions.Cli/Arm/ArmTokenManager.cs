using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Azure.Functions.Cli.Common;
using Colors.Net;
using System.Linq;
using Azure.Functions.Cli.Extensions;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using static Colors.Net.StringStaticMethods;
using System.Net.Http.Headers;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Arm
{
    internal class ArmTokenManager : IArmTokenManager
    {
        private static readonly ArmTokenCache _tokenCache;
        private readonly ISettings _settings;

        static ArmTokenManager()
        {
            _tokenCache = new ArmTokenCache();
        }

        public ArmTokenManager(ISettings settings)
        {
            _settings = settings;
        }

        public async Task<IEnumerable<string>> GetTenants()
        {
            if (_tokenCache.Count == 0)
            {
                await Login();
            }
            return _tokenCache.ReadItems().Select(i => i.TenantId);
        }

        public Task<string> GetToken(string tenantId)
        {
            return GetToken(tenantId, catchException: true);
        }

        public async Task<string> GetToken(string tenantId, bool catchException)
        {
            try
            {
                var authContext = new AuthenticationContext($"{Constants.ArmConstants.AADAuthorityBase}/{tenantId}", _tokenCache);
                var result = await authContext.AcquireTokenSilentAsync(Constants.ArmConstants.ArmAudience, Constants.ArmConstants.AADClientId);
                return result.AccessToken;
            }
            catch
            {
                if (catchException)
                {
                    await Login();
                    return await GetToken(tenantId, false);
                }
                throw;
            }
        }

        public async Task Login()
        {
            var authContext = new AuthenticationContext(Constants.ArmConstants.CommonAADAuthority, _tokenCache);
            _tokenCache.Clear();
            var code = await authContext.AcquireDeviceCodeAsync(Constants.ArmConstants.ArmAudience, Constants.ArmConstants.AADClientId);

            ColoredConsole
                .WriteLine($"To sign in, use a web browser to open the page {code.VerificationUrl} and enter the code {code.UserCode} to authenticate.");

            var token = await authContext.AcquireTokenByDeviceCodeAsync(code);
            ColoredConsole.WriteLine(Gray($"Logging into {token.TenantId}"));
            if (string.IsNullOrEmpty(_settings.CurrentTenant))
            {
                _settings.CurrentTenant = token.TenantId;
            }

            var tenants = await AcquireTenants(token.AccessToken);
            var tokens = await tenants.Select(tenant =>
            {
                ColoredConsole.WriteLine(Gray($"Logging into {tenant}"));
                authContext = new AuthenticationContext($"{Constants.ArmConstants.AADAuthorityBase}/{tenant}", _tokenCache);
                try
                {
                    return authContext.AcquireTokenSilentAsync(Constants.ArmConstants.ArmAudience, Constants.ArmConstants.AADClientId);
                }
                catch
                {
                    ColoredConsole.WriteLine($"Error logging into tenant {tenant}. Try to logout and login again.");
                    return Task.FromResult<AuthenticationResult>(null);
                }
            }).WhenAll();

            if (!_settings.CurrentTenant.Equals(token.TenantId, StringComparison.OrdinalIgnoreCase) &&
                !tokens.Any(t => t.TenantId.Equals(_settings.CurrentTenant)))
            {
                _settings.CurrentTenant = token.TenantId;
            }
        }

        private async Task<IEnumerable<string>> AcquireTenants(string token)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(Constants.ArmConstants.ArmDomain);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await client.GetAsync("/tenants?api-version=2014-06-01");
                response.EnsureSuccessStatusCode();
                var tenants = await response.Content.ReadAsAsync<JObject>();
                if (tenants["value"]?.Type == JTokenType.Array)
                {
                    return tenants["value"]
                        .Select(i => i["tenantId"]?.ToString())
                        .Where(t => !string.IsNullOrEmpty(t));
                }
                else
                {
                    return Enumerable.Empty<string>();
                }
            }
        }

        public void Logout()
        {
            _tokenCache.Clear();
        }
    }
}