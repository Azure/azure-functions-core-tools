using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BaseAzureAction : BaseAction, IInitializableAction
    {
        private string _getAccessTokenPowerShellScript = @"
if(Get-Module -ListAvailable Az.Profile) {
    $currentAzureContext = Get-AzContext;
} elseif (Get-Module -ListAvailable AzureRm.Profile) {
    $currentAzureContext = Get-AzureRmContext;
} else {
    throw 'Unable to locate Az or AzureRm. Please install the Az module and try again.';
}
$azureRmProfile = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile;
$profileClient = New-Object Microsoft.Azure.Commands.ResourceManager.Common.RMProfileClient($azureRmProfile);
$profileClient.AcquireAccessToken($currentAzureContext.Subscription.TenantId).AccessToken;
";

        public string AccessToken { get; set; }
        public bool ReadStdin { get; set; }

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

            return base.ParseArgs(args);
        }

        public async Task Initialize()
        {
            if (!string.IsNullOrEmpty(AccessToken))
            {
                return;
            }
            else if (ReadStdin && System.Console.In != null)
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
            else
            {
                AccessToken = await GetAccessToken();
            }
        }

        private async Task<string> GetAccessToken()
        {
            return await AzureCliGetToken();
        }

        private async Task<string> AzureCliGetToken()
        {
            if (CommandChecker.CommandExists("az"))
            {
                var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? new Executable("cmd", "/c az account get-access-token --query \"accessToken\" --output json")
                    : new Executable("az", "account get-access-token --query \"accessToken\" --output json");

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
                if (exitCode != 0)
                {
                    throw new CliException(stderr.ToString().Trim(' ', '\n', '\r') + $"{Environment.NewLine}" + "Make sure to run \"az login\" to log in to Azure and retry this command.");
                }
                else
                {
                    return stdout.ToString().Trim(' ', '\n', '\r', '"');
                }
            }
            else if (CommandChecker.CommandExists("pwsh"))
            {
                
                var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? new Executable("cmd", $"/c pwsh -NonInteractive -o Text -NoProfile -c {_getAccessTokenPowerShellScript}")
                    : new Executable("pwsh", $"-NonInteractive -o Text -NoProfile -c {_getAccessTokenPowerShellScript}");

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
                if (exitCode != 0)
                {
                    throw new CliException(stderr.ToString().Trim(' ', '\n', '\r') + $"{Environment.NewLine}" + "Make sure to run \"Login-AzAccount\" or \"Login-AzureRmAccount\" to log in to Azure and retry this command.");
                }
                else
                {
                    return stdout.ToString().Trim(' ', '\n', '\r', '"');
                }
            }
            else if (CommandChecker.CommandExists("powershell"))
            {
                var az = new Executable("cmd", $"/c powershell -NonInteractive -o Text -NoProfile -c {_getAccessTokenPowerShellScript}");

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
                if (exitCode != 0)
                {
                    throw new CliException(stderr.ToString().Trim(' ', '\n', '\r') + $"{Environment.NewLine}" + "Make sure to run \"Login-AzAccount\" or \"Login-AzureRmAccount\" to log in to Azure and retry this command.");
                }
                else
                {
                    return stdout.ToString().Trim(' ', '\n', '\r', '"');
                }
            }
            else
            {
                throw new FileNotFoundException("Cannot find az cli or Azure PowerShell. Please make sure to install az cli or Azure PowerShell.");
            }
        }
    }
}
