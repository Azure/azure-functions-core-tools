using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.Constants;
using static Azure.Functions.Cli.Extensions.StringExtensions;
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

                // Delete temporary file created to pass data in proper format to az CLI
                File.Delete($"{tempFile}");

                if (exitCode != 0)
                {
                    throw new CliException(stderr.ToString().Trim(' ', '\n', '\r', '"'));
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
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ALLOWED_AUDIENCES", serializedReplyUrls);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_AUTO_AAD", "True");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_CLIENT_ID", clientId);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_CLIENT_SECRET", clientSecret);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_DEFAULT_PROVIDER", "AzureActiveDirectory");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ENABLED", "True");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_OPENID_ISSUER", "https://sts.windows.net/" + tenant + "/");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_RUNTIME_VERSION", "1.0.0");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_TOKEN_STORE", "True");
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_UNAUTHENTICATED_ACTION", "AllowAnonymous");

            // Create signing and encryption keys for local testing
            string encryptionKey = ComputeSha256Hash(clientSecret);
            string signingKey = ComputeSha256Hash(clientId);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ENCRYPTION_KEY", encryptionKey);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_SIGNING_KEY", signingKey);            
            MiddlewareAuthSettings.Commit();

            // We also need to add this file to the .csproj so that it copies to \bin\debug\netstandard2.x when the Function builds
            var csProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.csproj").ToList();

            if (csProjFiles.Count == 1)
            {
                ModifyCSProj(csProjFiles.First());
                return;
            }
            else if (csProjFiles.Count == 0)
            {
                // The working directory might be \bin\debug\netstandard2.x
                // Try going up three levels to the main Function directory
                var functionDir = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, @"..\..\..\"));
                var functionDirProjFiles = FileSystemHelpers.GetFiles(functionDir, searchPattern: "*.csproj").ToList();

                if (functionDirProjFiles.Count == 1)
                {
                    ModifyCSProj(functionDirProjFiles.First());
                    return;
                }
            }

            throw new CliException($"Need to be in same folder as .csproj file. Expected 1 .csproj but found {csProjFiles.Count}");
        }

        /// <summary>
        /// Modify the Function's .csproj so the middleware auth json file will copy to the output directory
        /// </summary>
        public static void ModifyCSProj(string csProj)
        {
            var xmlFile = XDocument.Load(csProj);

            var project = xmlFile.Element("Project");
            var itemGroups = project.Elements("ItemGroup");

            var existing = itemGroups.Elements("None").FirstOrDefault(elm => elm.Attribute("Update").Value.Equals(SecretsManager.MiddlewareAuthSettingsFileName));
            if (existing != null)
            {
                // If we've previously added this file to the .csproj during a previous create-aad call, do not add again
                return;
            }

            // Assemble the attribute
            var newItemGroup = new XElement("ItemGroup");
            var noneElement = new XElement("None", new XAttribute("Update", SecretsManager.MiddlewareAuthSettingsFileName));
            noneElement.Add(new XElement("CopyToOutputDirectory", "PreserveNewest"));
            noneElement.Add(new XElement("CopyToPublishDirectory", "Never"));
            newItemGroup.Add(noneElement);

            // append item group to project, rather than modifying existing item group
            project.Add(newItemGroup);
            xmlFile.Save(csProj);
            ColoredConsole.WriteLine(Yellow($"Modified {csProj} to include {SecretsManager.MiddlewareAuthSettingsFileName} in output directory."));
        }

        public Dictionary<string, string> CreateAuthSettingsToPublish(string homepage, string clientId, string clientSecret, string tenant, List<string> replyUrls)
        {
            // The 'allowedAudiences' setting of /config/authsettings is of the form ["{replyURL1}", "{replyURL2}"]
            string serializedArray = JsonConvert.SerializeObject(replyUrls, Formatting.Indented);

            var authSettingsToPublish = new Dictionary<string, string>
            {
                { "allowedAudiences", serializedArray },
                { "isAadAutoProvisioned", "True" },
                { "clientId", clientId },
                { "clientSecret", clientSecret },
                { "defaultProvider", "0" }, // 0 corresponds to AzureActiveDirectory
                { "enabled", "True" },
                { "issuer", "https://sts.windows.net/" + tenant + "/" },
                { "runtimeVersion", "1.0.0" },
                { "tokenStoreEnabled", "True" },
                { "unauthenticatedClientAction", "1" } // Corresponds to AllowAnonymous
            };

            return authSettingsToPublish;
        }
    
        private static async Task<bool> PublishAuthSettingAsync(Site functionApp, string accessToken, Dictionary<string, string> authSettings)
        {
            functionApp.AzureAuthSettings = authSettings;
            var result = await AzureHelper.UpdateFunctionAppAuthSettings(functionApp, accessToken);
            if (!result.IsSuccessful)
            {
                throw new CliException((result.ErrorResult));
            }
            return true;
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