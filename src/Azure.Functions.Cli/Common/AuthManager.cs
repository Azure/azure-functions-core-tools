using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Common
{
    internal class AuthManager : IAuthManager
    {
        public AuthManager() { }

        private AuthSettingsFile MiddlewareAuthSettings;

        public async Task CreateAADApplication(string accessToken, string AADName, string appName)
        {
            if (string.IsNullOrEmpty(AADName))
            {
                throw new CliArgumentsException("Must specify name of new Azure Active Directory application with --aad-name parameter.",
                    new CliArgument { Name = "app-name", Description = "Name of new Azure Active Directory application" });
            }

            if (CommandChecker.CommandExists("az"))
            {
                List<string> replyUrls;
                string tempFile, clientSecret, query = CreateQuery(AADName, appName, out tempFile, out clientSecret, out replyUrls);

                ColoredConsole.WriteLine("Query successfully constructed. Creating new Azure AD Application now..");

                var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                       ? new Executable("cmd", $"/c az ad app create {query}")
                       : new Executable("az", $"ad app create {query}");
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();

                int exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));               

                // Clean up file we created to pass data in proper format to az CLI
                File.Delete($"{tempFile}");

                if (exitCode != 0)
                {
                    ColoredConsole.WriteLine(Red(stderr.ToString().Trim(' ', '\n', '\r', '"')));
                    return;
                }

                string response = stdout.ToString().Trim(' ', '\n', '\r', '"');
                ColoredConsole.WriteLine(Green(response));

                JObject application = JObject.Parse(response);
                var jwt = new JwtSecurityToken(accessToken);
                string tenantId = jwt.Payload["tid"] as string;
                string clientId = (string)application["appId"];
                string homepage = (string)application["homepage"];

                if (appName == null)
                {
                    // Update function application's (local) auth settings
                    CreateAndCommitAuthSettings(homepage, clientId, clientSecret, tenantId, replyUrls);                    
                    ColoredConsole.WriteLine(Yellow($"This application will only work for the Function Host default port of {StartHostAction.DefaultPort}"));
                }
                else
                {
                    // Connect this AAD application to the Site whose name was supplied
                    // Sets the site's /config/authsettings
                    var connectedSite = await AzureHelper.GetFunctionApp(appName, accessToken);
                    var authSettingsToPublish = CreateAuthSettingsToPublish(homepage, clientId, clientSecret, tenantId, replyUrls);
                    await PublishAuthSettingAsync(connectedSite, accessToken, authSettingsToPublish);
                }
            }
            else
            {
                throw new FileNotFoundException("Cannot find az cli. `auth create-aad` requires the Azure CLI.");
            }
        }

        /// <summary>
        /// Create the query to send to az ad app create
        /// </summary>
        /// <param name="AADName">Name of the new AAD application</param>
        /// <param name="appName">Name of an existing Azure Application to link to this AAD application</param>
        /// <returns></returns>
        public string CreateQuery(string AADName, string appName, out string tempFile, out string clientSecret, out List<string> replyUrls)
        {
            clientSecret = GeneratePassword(128);
            string authCallback = "/.auth/login/aad/callback";

            // Assemble the required resources in the proper format
            var resourceList = new List<requiredResourceAccess>();
            var access = new requiredResourceAccess();
            access.resourceAppId = AADConstants.ServicePrincipals.AzureADGraph;
            access.resourceAccess = new resourceAccess[]
            {
                    new resourceAccess {  type = AADConstants.ResourceAccessTypes.User, id = AADConstants.Permissions.EnableSSO.ToString() }
            };

            resourceList.Add(access);

            // It is easiest to pass them in the right format to the az CLI via a (temp) file + filename
            tempFile = $"{Guid.NewGuid()}.txt";
            File.WriteAllText(tempFile, JsonConvert.SerializeObject(resourceList));

            // Based on whether or not this AAD application is to be used in production or a local environment,
            // these parameters are different (plus reply URLs):
            string identifierUrl, homepage;

            // This AAD application is for local development - use localhost reply URLs, create local.middleware.json
            if (appName == null)
            {               
                // OAuth is port sensitive. There is no way of using a wildcard in the reply URLs to allow for variable ports
                // Set the port in the reply URLs to the default used by the Functions Host
                identifierUrl = "https://" + AADName + ".localhost";
                homepage = "http://localhost:" + StartHostAction.DefaultPort;                
                string localhostSSL = "https://localhost:" + StartHostAction.DefaultPort + authCallback;
                string localhost = "http://localhost:" + StartHostAction.DefaultPort + authCallback;

                replyUrls = new List<string>
                {
                    localhostSSL,
                    localhost
                };               
            }
            else
            {
                identifierUrl = "https://" + appName + ".azurewebsites.net";
                homepage = identifierUrl;
                string replyUrl = homepage + authCallback;

                replyUrls = new List<string>
                {
                    replyUrl
                };
            }

            replyUrls.Sort();
            string serializedReplyUrls = string.Join(" ", replyUrls.ToArray<string>());

            string query = $"--display-name {AADName} --homepage {homepage} --identifier-uris {identifierUrl} --password {clientSecret}" +
                    $" --reply-urls {serializedReplyUrls} --oauth2-allow-implicit-flow true --required-resource-access @{tempFile}";

            return query;
        }

        public void CreateAndCommitAuthSettings(string homepage, string clientId, string clientSecret, string tenant, List<string> replyUrls)
        {
            // The WEBSITE_AUTH_ALLOWED_AUDIENCES setting is of the form "{replyURL1} {replyURL2}"
            string serializedReplyUrls = string.Join(" ", replyUrls.ToArray<string>());

            // Create a local auth .json file that will be used by the middleware
            var middlewareAuthSettingsFile = SecretsManager.MiddlewareAuthSettingsFileName;
            MiddlewareAuthSettings = new AuthSettingsFile(middlewareAuthSettingsFile);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_AUTO_AAD", "True");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_CLIENT_ID", clientId);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_CLIENT_SECRET", clientSecret);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_DEFAULT_PROVIDER", "AzureActiveDirectory");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ENABLED", "True");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_OPENID_ISSUER", "https://sts.windows.net/" + tenant + "/");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_RUNTIME_VERSION", "1.0.0");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_TOKEN_STORE", "True");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_UNAUTHENTICATED_ACTION", "AllowAnonymous");

            // Middleware requires signing and encryption keys for local testing
            // These will be different than the encryption and signing keys used by the application in production
            string encryptionKey = ComputeSha256Hash(clientSecret);
            string signingKey = ComputeSha256Hash(clientId);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ENCRYPTION_KEY", encryptionKey);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_SIGNING_KEY", signingKey);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ALLOWED_AUDIENCES", serializedReplyUrls);
            MiddlewareAuthSettings.Commit();
        }

        public Dictionary<string, string> CreateAuthSettingsToPublish(string homepage, string clientId, string clientSecret, string tenant, List<string> replyUrls)
        {
            // The 'allowedAudiences' setting of /config/authsettings is of the form ["{replyURL1}", "{replyURL2}"]
            string serializedArray = JsonConvert.SerializeObject(replyUrls, Formatting.Indented);

            var authSettingsToPublish = new Dictionary<string, string>();
            authSettingsToPublish.Add("allowedAudiences", serializedArray);
            authSettingsToPublish.Add("isAadAutoProvisioned", "True");
            authSettingsToPublish.Add("clientId", clientId);
            authSettingsToPublish.Add("clientSecret", clientSecret);
            authSettingsToPublish.Add("defaultProvider", "0"); // 0 corresponds to AzureActiveDirectory
            authSettingsToPublish.Add("enabled", "True");
            authSettingsToPublish.Add("issuer", "https://sts.windows.net/" + tenant + "/");
            authSettingsToPublish.Add("runtimeVersion", "1.0.0");
            authSettingsToPublish.Add("tokenStoreEnabled", "True");
            authSettingsToPublish.Add("unauthenticatedClientAction", "1"); // Corresponds to AllowAnonymous

            return authSettingsToPublish;
        }
    
        private static async Task<bool> PublishAuthSettingAsync(Site functionApp, string accessToken, Dictionary<string, string> authSettings)
        {
            functionApp.AzureAuthSettings = authSettings;
            var result = await AzureHelper.UpdateFunctionAppAuthSettings(functionApp, accessToken);
            if (!result.IsSuccessful)
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("Error updating app settings:"))
                    .WriteLine(ErrorColor(result.ErrorResult));
                return false;
            }
            return true;
        }

        static string ComputeSha256Hash(string rawData)
        {
            // Create a SHA256   
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                // Convert byte array to a string   
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public static string GeneratePassword(int length)
        {
            const string PasswordChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHJKLMNPQRSTWXYZ0123456789#$";
            string pwd = GetRandomString(PasswordChars, length);

            while (!MeetsConstraint(pwd))
            {
                pwd = GetRandomString(PasswordChars, length);
            }

            return pwd;
        }

        private static string GetRandomString(string allowedChars, int length)
        {
            StringBuilder retVal = new StringBuilder(length);
            byte[] randomBytes = new byte[length * 4];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(randomBytes);

                for (int i = 0; i < length; i++)
                {
                    int seed = BitConverter.ToInt32(randomBytes, i * 4);
                    Random random = new Random(seed);
                    retVal.Append(allowedChars[random.Next(allowedChars.Length)]);
                }
            }

            return retVal.ToString();
        }

        private static bool MeetsConstraint(string password)
        {
            return !string.IsNullOrEmpty(password) &&
                password.Any(c => char.IsUpper(c)) &&
                password.Any(c => char.IsLower(c)) &&
                password.Any(c => char.IsDigit(c)) &&
                password.Any(c => !char.IsLetterOrDigit(c));
        }
    }

    static class AADConstants
    {
        public static class ServicePrincipals
        {
            public const string AzureADGraph = "00000002-0000-0000-c000-000000000000";
        }

        public static class Permissions
        {
            public static readonly Guid AccessApplication = new Guid("92042086-4970-4f83-be1c-e9c8e2fab4c8");
            public static readonly Guid EnableSSO = new Guid("311a71cc-e848-46a1-bdf8-97ff7156d8e6");
            public static readonly Guid ReadDirectoryData = new Guid("5778995a-e1bf-45b8-affa-663a9f3f4d04");
            public static readonly Guid ReadAndWriteDirectoryData = new Guid("78c8a3c8-a07e-4b9e-af1b-b5ccab50a175");
        }

        public static class ResourceAccessTypes
        {
            public const string Application = "Role";
            public const string User = "Scope";
        }
    }

    class resourceAccess
    {
        public string id { get; set; }
        public string type { get; set; }
    }

    class requiredResourceAccess
    {
        public string resourceAppId { get; set; }
        public resourceAccess[] resourceAccess { get; set; }
    }
}