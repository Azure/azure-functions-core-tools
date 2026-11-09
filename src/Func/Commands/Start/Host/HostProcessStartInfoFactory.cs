// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed class HostProcessStartInfoFactory
{
    public const int DefaultPort = 7071;
    public const string ExecutableBaseName = "Azure.Functions.Cli.Workloads.Host";
    public const string ScriptRootEnvironmentVariable = "AzureWebJobsScriptRoot";
    public const string AzureFunctionsEnvironmentVariable = "AZURE_FUNCTIONS_ENVIRONMENT";
    public const string WebsiteHostnameEnvironmentVariable = "WEBSITE_HOSTNAME";
    public const string AspNetCoreSuppressStatusMessagesEnvironmentVariable = "ASPNETCORE_SUPPRESSSTATUSMESSAGES";
    public const string HostingLifetimeLogLevelEnvironmentVariable = "Logging__LogLevel__Microsoft.Hosting.Lifetime";

    public HostProcessLaunchInfo Create(HostProcessStartContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string executablePath = ResolveExecutablePath(context.HostWorkload.ContentRoot);
        if (!File.Exists(executablePath))
        {
            throw new GracefulException(
                $"Host workload executable was not found at '{executablePath}'. Run 'func workload install host --force' to repair the install.",
                isUserError: true);
        }

        int port = ResolvePort(context.Options.Port);
        IPAddress address = ResolveAddress(context.Options.Address);
        string listenUriText = string.Create(
            CultureInfo.InvariantCulture,
            $"http://{new IPEndPoint(address, port)}");
        var listenUri = new Uri(listenUriText);
        bool useLocalhost = address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback)
            || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
        var localBaseUri = new UriBuilder(listenUri)
        {
            Host = useLocalhost ? "localhost" : address.ToString(),
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = context.HostRunContext.StartupDirectory.FullName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (context.Options.EnableAuth)
        {
            startInfo.ArgumentList.Add("--enable-auth");
        }

        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(listenUriText);

        foreach (KeyValuePair<string, string> pair in context.HostRunContext.EnvironmentVariables)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        startInfo.Environment[ScriptRootEnvironmentVariable] = context.HostRunContext.StartupDirectory.FullName;
        startInfo.Environment[AzureFunctionsEnvironmentVariable] = "Development";
        if (port == 0)
        {
            // The actual authority is unknown until the host binds.
            startInfo.Environment.Remove(WebsiteHostnameEnvironmentVariable);
        }
        else
        {
            startInfo.Environment[WebsiteHostnameEnvironmentVariable] = localBaseUri.Uri.Authority;
        }
        startInfo.Environment.TryAdd(AspNetCoreSuppressStatusMessagesEnvironmentVariable, "true");
        startInfo.Environment[HostingLifetimeLogLevelEnvironmentVariable] = "None";

        return new HostProcessLaunchInfo(startInfo, port, listenUri,
            localBaseUri.Uri, context.HostWorkload.PackageVersion);
    }

    internal static string ResolveExecutablePath(string contentRoot)
    {
        string executableName = OperatingSystem.IsWindows()
            ? $"{ExecutableBaseName}.exe"
            : ExecutableBaseName;

        return Path.Combine(contentRoot, executableName);
    }

    private static int ResolvePort(int? port)
    {
        int resolved = port ?? DefaultPort;
        if (resolved is < 0 or > 65535)
        {
            throw new GracefulException("--port must be between 0 and 65535. Use 0 to let the OS assign an available port.", isUserError: true);
        }

        return resolved;
    }

    private static IPAddress ResolveAddress(string? address)
    {
        if (address is null)
        {
            return IPAddress.Loopback;
        }

        if (!IPAddress.TryParse(address, out IPAddress? parsed))
        {
            throw new GracefulException(
                $"Invalid --address or Host.LocalHttpAddress value '{address}'. Specify an IP address such as 127.0.0.1 or 0.0.0.0.",
                isUserError: true);
        }

        return parsed;
    }
}
