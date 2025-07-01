// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    internal abstract class BaseAzureAction : BaseAction, IInitializableAction
    {
        // Az is the Azure PowerShell module that works in both PowerShell Core and Windows PowerShell
        private const string AzProfileModuleName = "Az.Accounts";

        // AzureRm is the Azure PowerShell module that only works on Windows PowerShell
        private const string AzureRmProfileModuleName = "AzureRM.Profile";

        // PowerShell Core is version 6.0 and higher that is cross-platform
        private const string PowerShellCoreExecutable = "pwsh";

        // Windows PowerShell is PowerShell version 5.1 and lower that only works on Windows
        private const string WindowsPowerShellExecutable = "powershell";

        private const string DefaultManagementURL = Constants.DefaultManagementURL;

        public string AccessToken { get; set; }

        public bool ReadStdin { get; set; }

        public string ManagementURL { get; set; }

        public string Subscription { get; private set; }

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

            if (string.IsNullOrEmpty(AccessToken))
            {
                AccessToken = await GetAccessToken();
            }

            if (string.IsNullOrEmpty(ManagementURL))
            {
                ManagementURL = await GetManagementURL();
            }
        }

        private async Task<string> GetManagementURL()
        {
            (bool azCliSucceeded, string managementURL) = await TryGetAzCLIManagementURL();
            if (!azCliSucceeded)
            {
                // TODO: Try with Poweshell if az is non-existent or if the call fails
                // For now, let's default this out
                managementURL = DefaultManagementURL;
            }

            // Having a trailing slash could cause issues later when we attach it to function IDs
            // It's easier to remove now, than to do that before every ARM call.
            return managementURL.EndsWith("/") ? managementURL[..^1] : managementURL;
        }

        private async Task<(bool AzCliSucceeded, string ManagementURL)> TryGetAzCLIManagementURL()
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
            (bool cliSucceeded, string cliToken) = await TryGetAzCliToken();

            if (cliSucceeded)
            {
                return cliToken;
            }

            (bool powershellSucceeded, string psToken) = await TryGetAzPowerShellToken();

            if (powershellSucceeded)
            {
                return psToken;
            }

            if (TryGetTokenFromTestEnvironment(out string envToken))
            {
                return envToken;
            }

            throw new CliException($"Unable to connect to Azure. Make sure you have the `az` CLI or `{AzProfileModuleName}` PowerShell module installed and logged in and try again");
        }

        private bool TryGetTokenFromTestEnvironment(out string token)
        {
            token = Environment.GetEnvironmentVariable(Constants.AzureManagementAccessToken);
            return !string.IsNullOrEmpty(token);
        }

        private async Task<(bool Succeeded, string Token)> TryGetAzCliToken()
        {
            try
            {
                return (true, await RunAzCLICommand("account get-access-token --query \"accessToken\" --output json"));
            }
            catch (Exception)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(WarningColor("Unable to fetch access token from az CLI"));
                }

                return (false, null);
            }
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

        private async Task<(bool Succeeded, string Token)> TryGetAzPowerShellToken()
        {
            // PowerShell Core can only use Az so we can check that it exists and that the Az module exists
            if (CommandChecker.CommandExists(PowerShellCoreExecutable) &&
                await CommandChecker.PowerShellModuleExistsAsync(PowerShellCoreExecutable, AzProfileModuleName))
            {
                var az = new Executable(PowerShellCoreExecutable, $"-NonInteractive -o Text -NoProfile -c {GetPowerShellAccessTokenScript(AzProfileModuleName)}");

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
            if (CommandChecker.CommandExists(WindowsPowerShellExecutable))
            {
                string moduleToUse;

                // depending on if Az.Profile or AzureRM.Profile is available, we need to change the prefix
                if (await CommandChecker.PowerShellModuleExistsAsync(WindowsPowerShellExecutable, AzProfileModuleName))
                {
                    moduleToUse = AzProfileModuleName;
                }
                else if (await CommandChecker.PowerShellModuleExistsAsync(WindowsPowerShellExecutable, AzureRmProfileModuleName))
                {
                    moduleToUse = AzureRmProfileModuleName;
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
        private static string GetPowerShellAccessTokenScript(string module)
        {
            string prefix;
            if (module == AzProfileModuleName)
            {
                prefix = "Az";
            }
            else if (module == AzureRmProfileModuleName)
            {
                prefix = "AzureRM";
            }
            else
            {
                throw new ArgumentException($"Expected module to be '{AzProfileModuleName}' or '{AzureRmProfileModuleName}'");
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
