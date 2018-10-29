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

        private WorkerRuntime _workerRuntime;

        private static string _requiredResourceFilename = "requiredResourceAccessList.txt";

        public async Task CreateAADApplication(string accessToken, string AADAppRegistrationName, WorkerRuntime workerRuntime, string appName)
        {
            if (string.IsNullOrEmpty(AADAppRegistrationName))
            {
                throw new CliArgumentsException("Must provide name for a new Azure Active Directory app registration with --AADAppRegistrationName parameter.",
                    new CliArgument { Name = "AADAppRegistrationName", Description = "Name of new Azure Active Directory app registration" });
            }

            if (CommandChecker.CommandExists("az"))
            {
                _workerRuntime = workerRuntime;
                _requiredResourceFilename = string.Format("{0}-{1}", AADAppRegistrationName, _requiredResourceFilename);

                List<string> replyUrls;
                string clientSecret, hostName, commandLineArgs = GetCommandLineArguments(AADAppRegistrationName, appName, out clientSecret, out replyUrls, out hostName);

                string command = $"ad app create {commandLineArgs}";
                ColoredConsole.WriteLine($"Creating new Azure AD Application via:\n" +
                    $"az {command}");

                var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                       ? new Executable("cmd", $"/c az {command}")
                       : new Executable("az", command);


                var stdout = new StringBuilder();
                var stderr = new StringBuilder();

                int exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));               

                if (exitCode != 0)
                {
                    throw new CliException(stderr.ToString().Trim(' ', '\n', '\r', '"'));
                }

                string response = stdout.ToString().Trim(' ', '\n', '\r', '"');
                ColoredConsole.WriteLine(Green($"Successfully created new AAD registration {AADAppRegistrationName}"));
                ColoredConsole.WriteLine(White(response));

                JObject application = JObject.Parse(response);
                var jwt = new JwtSecurityToken(accessToken);
                string tenantId = jwt.Payload["tid"] as string;
                string clientId = (string)application["appId"];

                if (appName == null)
                {
                    // Update function application's (local) auth settings
                    CreateAndCommitAuthSettings(hostName, clientId, clientSecret, tenantId, replyUrls);
                }
                else
                {
                    // Connect this AAD application to the Site whose name was supplied (set site's /config/authsettings)
                    // Tell customer what we're doing, since finding and updating the site can take a number of seconds
                    ColoredConsole.WriteLine($"\nUpdating auth settings of application {appName}..");

                    var connectedSite = await AzureHelper.GetFunctionApp(appName, accessToken);
                    var authSettingsToPublish = CreateAuthSettingsToPublish(clientId, clientSecret, tenantId, replyUrls);

                    await PublishAuthSettingAsync(connectedSite, accessToken, authSettingsToPublish);

                    ColoredConsole.WriteLine(Green($"Successfully updated {appName}'s auth settings to reference new AAD app registration {AADAppRegistrationName}"));
                }
            }
            else
            {
                throw new FileNotFoundException("Cannot find az cli. `auth create-aad` requires the Azure CLI.");
            }
        }

        /// <summary>
        /// Create the string of arguments to send to az ad app create
        /// </summary>
        /// <param name="AADAppRegistrationName">Name of the new AAD application</param>
        /// <param name="appName">Name of an existing Azure Application to link to this AAD application</param>
        /// <returns></returns>
        private string GetCommandLineArguments(string AADAppRegistrationName, string appName, out string clientSecret, out List<string> replyUrls, out string hostName)
        {
            clientSecret = GeneratePassword(128);
            string authCallback = "/.auth/login/aad/callback";

            var requiredResourceAccess = GetRequiredResourceAccesses();

            // It is easiest to pass them in the right format to the az CLI via a (temp) file + filename
            File.WriteAllText(_requiredResourceFilename, JsonConvert.SerializeObject(requiredResourceAccess));

            // Based on whether or not this AAD application is to be used in production or a local environment,
            // these parameters are different (plus reply URLs):
            string identifierUrl, homepage;

            // This AAD application is for local development - use localhost reply URLs, create local.middleware.json
            if (appName == null)
            {
                // OAuth is port sensitive. There is no way of using a wildcard in the reply URLs to allow for variable ports
                // Set the port in the reply URLs to the default used by the Functions Host
                identifierUrl = "http://" + AADAppRegistrationName + ".localhost";
                homepage = "http://localhost:" + StartHostAction.DefaultPort;
                hostName = "localhost:" + StartHostAction.DefaultPort;
                string localhostSSL = "https://localhost:" + StartHostAction.DefaultPort + authCallback;
                string localhost = "http://localhost:" + StartHostAction.DefaultPort + authCallback;

                replyUrls = new List<string>
                {                
                    localhost,
                    localhostSSL
                };               
            }
            else
            {
                identifierUrl = "https://" + appName + ".azurewebsites.net";
                homepage = identifierUrl;
                hostName = appName + ".azurewebsites.net";
                string replyUrl = homepage + authCallback;

                replyUrls = new List<string>
                {
                    replyUrl
                };
            }

            string serializedReplyUrls = string.Join(" ", replyUrls.ToArray<string>());

            return $"--display-name {AADAppRegistrationName} --homepage {homepage} --identifier-uris {identifierUrl} --password {clientSecret}" +
                    $" --reply-urls {serializedReplyUrls} --oauth2-allow-implicit-flow true --required-resource-access @{_requiredResourceFilename}";

        }

        private List<requiredResourceAccess> GetRequiredResourceAccesses()
        {
            var bindings = ExtensionsHelper.GetBindingsWithDirection();

            // Required for basic Easy Auth / Middleware authentication
            var resourceList = new List<requiredResourceAccess>
            {
                new requiredResourceAccess
                {
                    resourceAppId = AADConstants.ServicePrincipals.AzureADGraph,
                    resourceAccess = new resourceAccess[]
                    {
                        new resourceAccess
                        {
                            type = AADConstants.ResourceAccessTypes.User,
                            id = AADConstants.Permissions.EnableSSO.ToString()
                        }
                    }
                },
            };

            // Determine which Microsoft Graph permissions are necessary,
            // Based on which I/O bindings are in the user's functions
            HashSet<Guid> requiredPermissions = new HashSet<Guid>();

            foreach (var binding in bindings)
            {
                // Determine the required permissions for this binding
                if (AADConstants.PermissionMap.ContainsKey(binding))
                {
                    requiredPermissions.UnionWith(AADConstants.PermissionMap[binding]);
                }
            }

            var resourceAccessList = new List<resourceAccess>();
            foreach (var permission in requiredPermissions)
            {
                resourceAccessList.Add(new resourceAccess
                {
                    type = AADConstants.ResourceAccessTypes.User,
                    id = permission.ToString()
                });
            }

            if (resourceAccessList.Count > 0)
            {
                resourceList.Add(new requiredResourceAccess
                {
                    resourceAppId = AADConstants.ServicePrincipals.MicrosoftGraph,
                    resourceAccess = resourceAccessList.ToArray()
                });
            }

            return resourceList;
        }

        private void CreateAndCommitAuthSettings(string hostName, string clientId, string clientSecret, string tenant, List<string> replyUrls)
        {
            // The WEBSITE_AUTH_ALLOWED_AUDIENCES setting is of the form "{replyURL1} {replyURL2}"
            string serializedReplyUrls = string.Join(" ", replyUrls);

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
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_HOSTNAME", hostName);

            // Create signing and encryption keys for local testing
            string encryptionKey = ComputeSha256Hash(clientSecret);
            string signingKey = ComputeSha256Hash(clientId);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_ENCRYPTION_KEY", encryptionKey);
            MiddlewareAuthSettings.SetAuthSetting("WEBSITE_AUTH_SIGNING_KEY", signingKey);
            MiddlewareAuthSettings.Commit();

            ColoredConsole.WriteLine(Yellow($"Created {SecretsManager.MiddlewareAuthSettingsFileName} with authentication settings necessary for local development.\n" +
                $"Running this function locally will only work with the Function Host default port of {StartHostAction.DefaultPort}"));

            if (_workerRuntime == WorkerRuntime.dotnet)
            {
                // If this is a dotnet function, we also need to add this file to the .csproj 
                // so that it copies to \bin\debug\netstandard2.x when the Function builds
                string csProjFile = GetCSProjFilePath();

                if (csProjFile == null)
                {
                    throw new CliException($"Auth settings file {SecretsManager.MiddlewareAuthSettingsFileName} could not be added to a .csproj and will not be present in the the bin or output directories.");
                }

                ModifyCSProj(csProjFile);
            }
        }

        public static string GetCSProjFilePath()
        {
            // If we're in the Function root, the .csproj file will be in this folder
            var csProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.csproj").ToList();
            if (csProjFiles.Count == 1)
            {
                return csProjFiles.First();
            }

            // If we're in the function root\bin\debug\netstandard2.x, the .csproj file will be up three directories
            var functionDir = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, @"..\..\..\"));
                
            if (Directory.Exists(functionDir))
            {
                var functionDirProjFiles = FileSystemHelpers.GetFiles(functionDir, searchPattern: "*.csproj").ToList();

                if (functionDirProjFiles.Count == 1)
                {
                    return functionDirProjFiles.First();
                }
            }

            return null;
        }

        /// <summary>
        /// Modify the Function's .csproj so the middleware auth json file will copy to the output directory
        /// </summary>
        private static void ModifyCSProj(string csProj)
        {
            var xmlFile = XDocument.Load(csProj);

            var project = xmlFile.Element("Project");
            var itemGroups = project.Elements("ItemGroup");

            var existing = itemGroups.Elements("None").FirstOrDefault(elm => elm.Attribute("Update").Value.Equals(SecretsManager.MiddlewareAuthSettingsFileName));
            if (existing != null)
            {
                // If we've added this file to the .csproj during a previous create-aad call, do not add again
                return;
            }

            // Assemble the attribute
            var newItemGroup = new XElement("ItemGroup");
            var noneElement = new XElement("None", new XAttribute("Update", SecretsManager.MiddlewareAuthSettingsFileName));
            noneElement.Add(new XElement("CopyToOutputDirectory", "PreserveNewest"));
            noneElement.Add(new XElement("CopyToPublishDirectory", "Never"));
            newItemGroup.Add(noneElement);

            // Append item group to project & save
            project.Add(newItemGroup);
            xmlFile.Save(csProj);

            ColoredConsole.WriteLine(Yellow($"Modified {csProj} to include {SecretsManager.MiddlewareAuthSettingsFileName} in output directory."));
        }

        private Dictionary<string, string> CreateAuthSettingsToPublish(string clientId, string clientSecret, string tenant, List<string> replyUrls)
        {
            // The 'allowedAudiences' setting of /config/authsettings is of the form ["{replyURL1}", "{replyURL2}"]
            string serializedReplyUrls = JsonConvert.SerializeObject(replyUrls, Formatting.Indented);

            var authSettingsToPublish = new Dictionary<string, string>
            {
                { "allowedAudiences", serializedReplyUrls },
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