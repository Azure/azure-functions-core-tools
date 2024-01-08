using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using static Azure.Functions.Cli.Common.OutputTheme;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Azure.Identity;
using Azure.Core;
using System.Threading;
using Azure.ResourceManager;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BaseAzureAction : BaseAction, IInitializableAction
    {
        // Az is the Azure PowerShell module that works in both PowerShell Core and Windows PowerShell
        private const string _azProfileModuleName = "Az.Accounts";

        private const string _defaultManagementURL = Constants.DefaultManagementURL;

        private TokenCredential _credential;

        protected TokenCredential Credential => _credential ??= new ChainedTokenCredential(
            new AzureCliCredential(new AzureCliCredentialOptions { TenantId = TenantId }),
            new AzurePowerShellCredential(new AzurePowerShellCredentialOptions { TenantId = TenantId })
        );

        private ArmClient _armClient;

        protected ArmClient ArmClient => _armClient ??= new ArmClient(Credential, defaultSubscriptionId: "",
            new ArmClientOptions { Environment = new ArmEnvironment(new Uri(ManagementURL), ManagementURL) });

        public string AccessToken { get; set; }
        public bool ReadStdin { get; set; }
        public string ManagementURL { get; set; }
        public string Subscription { get; private set; }
        public string TenantId { get; private set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("access-token")
                .WithDescription("Access token to use for performing authenticated azure actions")
                .Callback(t => AccessToken = t);
            Parser
                .Setup<bool>("access-token-stdin")
                .WithDescription("Read access token from stdin e.g: az account get-access-token | func ... --access-token-stdin")
                .Callback(f => ReadStdin = f);
            Parser
                .Setup<string>("management-url")
                .WithDescription("Management URL for Azure Cloud e.g: --management-url https://management.azure.com.")
                .Callback(t => ManagementURL = t);
            Parser
                .Setup<string>("subscription")
                .WithDescription("Default subscription to use")
                .Callback(s => Subscription = s);
            Parser
                .Setup<string>("tenant-id")
                .WithDescription("Azure Tenant ID to use")
                .Callback(t => TenantId = t);

            return base.ParseArgs(args);
        }

        public async Task Initialize()
        {
            if (ReadStdin && System.Console.In != null)
            {
                var accessToken = System.Console.In.ReadToEnd().Trim(' ', '\n', '\r', '"');
                if (accessToken.StartsWith("{"))
                {
                    var json = JsonConvert.DeserializeObject<JObject>(accessToken);
                    AccessToken = json["accessToken"].ToString();
                }
                else
                {
                    AccessToken = accessToken;
                }
                if (string.IsNullOrEmpty(AccessToken))
                {
                    throw new CliException("Unable to set access token from stdin.");
                }
            }
            else if (ReadStdin && System.Console.In == null)
            {
                throw new CliException("Stdin unavailable");
            }

            if (string.IsNullOrEmpty(ManagementURL))
            {
                ManagementURL = await GetManagementURL();
            }

            if (string.IsNullOrEmpty(AccessToken))
            {
                AccessToken = await GetAccessToken();
            }
        }

        private async Task<string> GetManagementURL()
        {
            (bool azCliSucceeded, string managementURL) = await TryGetAzCLIManagementURL();
            if (!azCliSucceeded)
            {
                // TODO: Try with Poweshell if az is non-existent or if the call fails
                // For now, let's default this out
                managementURL = _defaultManagementURL;
            }
            // Having a trailing slash could cause issues later when we attach it to function IDs
            // It's easier to remove now, than to do that before every ARM call.
            return managementURL.EndsWith("/") ? managementURL.Substring(0, managementURL.Length - 1) : managementURL;
        }

        private async Task<(bool, string)> TryGetAzCLIManagementURL()
        {
            try
            {
                return (true, await RunAzCLICommand($"cloud list --query \"[?isActive].endpoints.resourceManager | [0]\" --output json"));
            }
            catch (Exception)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(WarningColor("Unable to retrieve the resource manager URL from az CLI"));
                }
                return (false, null);
            }
        }

        private async Task<string> GetAccessToken()
        {
            try
            {
                var accessToken = await Credential.GetTokenAsync(new TokenRequestContext(new[] { ManagementURL + "/.default" }), CancellationToken.None);
                return accessToken.Token;
            }
            catch (Exception ex)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(WarningColor("Unable to fetch access token from CLI"));
                    ColoredConsole.Error.WriteLine(ErrorColor(ex.ToString()));
                }

                if (TryGetTokenFromTestEnvironment(out string envToken))
                {
                    return envToken;
                }

                throw new CliException($"Unable to connect to Azure. Make sure you have the `az` CLI or `{_azProfileModuleName}` PowerShell module installed and logged in and try again");
            }
        }

        private bool TryGetTokenFromTestEnvironment(out string token)
        {
            token = Environment.GetEnvironmentVariable(Constants.AzureManagementAccessToken);
            return !string.IsNullOrEmpty(token);
        }

        private async Task<string> RunAzCLICommand(string param)
        {
            if (!CommandChecker.CommandExists("az"))
            {
                throw new CliException("az CLI not found");
            }
            var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new Executable("cmd", $"/c az {param}")
                : new Executable("az", param);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
            if (exitCode == 0)
            {
                return stdout.ToString().Trim(' ', '\n', '\r', '"');
            }
            else
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Unable to run az CLI command `az {param}`. Error: {stderr.ToString().Trim(' ', '\n', '\r')}"));
                }
                throw new CliException("Error running Az CLI command");
            }
        }
    }
}
