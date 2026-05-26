// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Globalization;
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
        string listenUriText = string.Create(
            CultureInfo.InvariantCulture,
            $"http://0.0.0.0:{port}");
        string localBaseUriText = string.Create(
            CultureInfo.InvariantCulture,
            $"http://localhost:{port}");

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
        startInfo.Environment[WebsiteHostnameEnvironmentVariable] = new Uri(localBaseUriText).Authority;
        startInfo.Environment.TryAdd(AspNetCoreSuppressStatusMessagesEnvironmentVariable, "true");
        startInfo.Environment[HostingLifetimeLogLevelEnvironmentVariable] = "None";

        return new HostProcessLaunchInfo(startInfo, port, new Uri(listenUriText),
            new Uri(localBaseUriText), context.HostWorkload.PackageVersion);
    }

    private static string ResolveExecutablePath(string contentRoot)
    {
        string executableName = OperatingSystem.IsWindows()
            ? $"{ExecutableBaseName}.exe"
            : ExecutableBaseName;

        return Path.Combine(contentRoot, executableName);
    }

    private static int ResolvePort(int? port)
    {
        int resolved = port ?? DefaultPort;
        if (resolved is < 1 or > 65535)
        {
            throw new GracefulException("--port must be between 1 and 65535.", isUserError: true);
        }

        return resolved;
    }
}
