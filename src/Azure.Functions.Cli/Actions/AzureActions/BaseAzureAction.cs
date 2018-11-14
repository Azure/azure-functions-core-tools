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
        private const string _getAccessTokenPowerShellScript = @"
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

        // Az is the Azure PowerShell module that works in both PowerShell Core and Windows PowerShell
        private const string _azProfileModuleName = "Az.Profile";

        // AzureRm is the Azure PowerShell module that only works on Windows PowerShell
        private const string _azureRmProfileModuleName = "AzureRM.Profile";

        // PowerShell Core is version 6.0 and higher that is cross-platform
        private const string _powerShellCoreExecutable = "pwsh";

        // Windows PowerShell is PowerShell version 5.1 and lower that only works on Windows
        private const string _windowsPowerShellExecutable = "powershell";

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
            // PowerShell Core can only load the Az module so we can check for both of those here.
            else if (CommandChecker.CommandExists(_powerShellCoreExecutable) &&
                await CommandChecker.PowerShellModuleExistsAsync(_powerShellCoreExecutable, _azProfileModuleName))
            {
                var az = new Executable(_powerShellCoreExecutable,
                    $"-NonInteractive -o Text -NoProfile -c {GetPowerShellAccessTokenScript(_azProfileModuleName)}");

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
                if (exitCode != 0)
                {
                    throw new CliException(stderr.ToString().Trim(' ', '\n', '\r') + $"{Environment.NewLine}" + "Make sure to run \"Login-AzAccount\" to log in to Azure and retry this command.");
                }
                else
                {
                    return stdout.ToString().Trim(' ', '\n', '\r', '"');
                }
            }
            // Windows PowerShell can use Az or AzureRM so first we check if powershell.exe is available
            else if (CommandChecker.CommandExists(_windowsPowerShellExecutable))
            {
                string scriptToRun;

                // depending on if Az.Profile or AzureRM.Profile is available, we need to change the prefix
                if (await CommandChecker.PowerShellModuleExistsAsync(_windowsPowerShellExecutable, _azProfileModuleName))
                {
                    scriptToRun = GetPowerShellAccessTokenScript(_azProfileModuleName);
                }
                else if (await CommandChecker.PowerShellModuleExistsAsync(_windowsPowerShellExecutable, _azureRmProfileModuleName))
                {
                    scriptToRun = GetPowerShellAccessTokenScript(_azureRmProfileModuleName);
                }
                else
                {
                    throw new FileNotFoundException("Cannot find az cli or Azure PowerShell. Please make sure to install az cli or Azure PowerShell.");
                }

                var az = new Executable("powershell", $"-NonInteractive -o Text -NoProfile -c {scriptToRun}");

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

        // Sets the prefix of the script in case they have Az.Profile or AzureRM.Profile
        private static string GetPowerShellAccessTokenScript (string module)
        {
            string prefix;
            if (module == _azProfileModuleName)
            {
                prefix = "Az";
            }
            else if (module == _azureRmProfileModuleName)
            {
                prefix = "AzureRM";
            }
            else
            {
                throw new ArgumentException($"Expected module to be '{_azProfileModuleName}' or '{_azureRmProfileModuleName}'");
            }

            // This PowerShell script first grabs the Azure context, fetches the profile client and requests an accesstoken.
            // This entirely done using the Az.Profile module or AzureRM.Profile
            return $@"
$currentAzureContext = Get-{prefix}Context;
$azureRmProfile = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile;
$profileClient = New-Object Microsoft.Azure.Commands.ResourceManager.Common.RMProfileClient($azureRmProfile);
$profileClient.AcquireAccessToken($currentAzureContext.Subscription.TenantId).AccessToken;
";
        }
    }
}
