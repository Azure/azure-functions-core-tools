using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using static Azure.Functions.Cli.Common.OutputTheme;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BaseAzureAction : BaseAction, IInitializableAction
    {
        // Az is the Azure PowerShell module that works in both PowerShell Core and Windows PowerShell
        private const string _azProfileModuleName = "Az.Accounts";

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
            (bool cliSucceeded, string cliToken) = await TryGetAzCliToken();
            if (cliSucceeded) return cliToken;

            (bool powershellSucceeded, string psToken) = await TryGetAzPowerShellToken();
            if (powershellSucceeded) return psToken;
            
            throw new CliException($"Unable to connect to Azure. Make sure you have the `az` CLI or `{_azProfileModuleName}` PowerShell installed and logged in and try again");
        }

        private async Task<(bool succeeded, string token)> TryGetAzCliToken()
        {
            if (CommandChecker.CommandExists("az"))
            {
                var az = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? new Executable("cmd", "/c az account get-access-token --query \"accessToken\" --output json")
                    : new Executable("az", "account get-access-token --query \"accessToken\" --output json");

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
                if (exitCode == 0)
                {
                    return (true, stdout.ToString().Trim(' ', '\n', '\r', '"'));
                }
                else
                {
                    if (StaticSettings.IsDebug)
                    {
                        ColoredConsole.WriteLine(VerboseColor($"Unable to fetch access token from az cli. Error: {stderr.ToString().Trim(' ', '\n', '\r')}"));
                    }
                }
            }
            return (false, null);
        }

        private async Task<(bool succeeded, string token)> TryGetAzPowerShellToken()
        {
            // PowerShell Core can only use Az so we can check that it exists and that the Az module exists
            if (CommandChecker.CommandExists(_powerShellCoreExecutable) &&
                await CommandChecker.PowerShellModuleExistsAsync(_powerShellCoreExecutable, _azProfileModuleName))
            {
                var az = new Executable(_powerShellCoreExecutable,
                    $"-NonInteractive -o Text -NoProfile -c {GetPowerShellAccessTokenScript(_azProfileModuleName)}");

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
                if (exitCode == 0)
                {
                    return (true, stdout.ToString().Trim(' ', '\n', '\r', '"'));
                }
                else
                {
                    if (StaticSettings.IsDebug)
                    {
                        ColoredConsole.WriteLine(VerboseColor($"Unable to fetch access token from Az.Profile in PowerShell Core. Error: {stderr.ToString().Trim(' ', '\n', '\r')}"));
                    }
                }
            }

            // Windows PowerShell can use Az or AzureRM so first we check if powershell.exe is available
            if (CommandChecker.CommandExists(_windowsPowerShellExecutable))
            {
                string moduleToUse;

                // depending on if Az.Profile or AzureRM.Profile is available, we need to change the prefix
                if (await CommandChecker.PowerShellModuleExistsAsync(_windowsPowerShellExecutable, _azProfileModuleName))
                {
                    moduleToUse = _azProfileModuleName;
                }
                else if (await CommandChecker.PowerShellModuleExistsAsync(_windowsPowerShellExecutable, _azureRmProfileModuleName))
                {
                    moduleToUse = _azureRmProfileModuleName;
                }
                else
                {
                    // User doesn't have either Az.Profile or AzureRM.Profile
                    if (StaticSettings.IsDebug)
                    {
                        ColoredConsole.WriteLine(VerboseColor("Unable to find Az.Profile or AzureRM.Profile."));
                    }
                    return (false, null);
                }

                var az = new Executable("powershell", $"-NonInteractive -o Text -NoProfile -c {GetPowerShellAccessTokenScript(moduleToUse)}");

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var exitCode = await az.RunAsync(o => stdout.AppendLine(o), e => stderr.AppendLine(e));
                if (exitCode == 0)
                {
                    return (true, stdout.ToString().Trim(' ', '\n', '\r', '"'));
                }
                else
                {
                    if (StaticSettings.IsDebug)
                    {
                        ColoredConsole.WriteLine(VerboseColor($"Unable to fetch access token from '{moduleToUse}'. Error: {stderr.ToString().Trim(' ', '\n', '\r')}"));
                    }
                }
            }
            return (false, null);
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
$profileClient = New-Object Microsoft.Azure.Commands.ResourceManager.Common.RMProfileClient $azureRmProfile;
$profileClient.AcquireAccessToken($currentAzureContext.Subscription.TenantId).AccessToken;
";
        }
    }
}
