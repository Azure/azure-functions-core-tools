// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Commands.Init;

public static class InitCommon
{
    public static readonly Dictionary<Lazy<string>, Task<string>> FileToContentMap = new Dictionary<Lazy<string>, Task<string>>
    {
        { new Lazy<string>(() => ".gitignore"), StaticResources.GitIgnore }
    };

    public static async Task WriteExtensionsJsonAsync(CancellationToken ct)
    {
        var file = Path.Combine(Environment.CurrentDirectory, ".vscode", "extensions.json");
        if (!FileSystemHelpers.DirectoryExists(Path.GetDirectoryName(file)))
        {
            FileSystemHelpers.CreateDirectory(Path.GetDirectoryName(file));
        }

        await FileSystemHelpers.WriteFileIfNotExists(file, await StaticResources.VsCodeExtensionsJson);
    }

    public static async Task SetupSourceControlAsync(CancellationToken ct)
    {
        try
        {
            var check = new Executable("git", "rev-parse --git-dir");
            var result = await check.RunAsync();
            if (result != 0)
            {
                var exe = new Executable("git", "init");
                await exe.RunAsync(l => Colors.Net.ColoredConsole.WriteLine(l), l => Colors.Net.ColoredConsole.Error.WriteLine(l));
            }
            else
            {
                Spectre.Console.AnsiConsole.WriteLine("Directory already a git repository.");
            }
        }
        catch (FileNotFoundException)
        {
            Spectre.Console.AnsiConsole.WriteLine(New.OutputTheme.WarningColor("Unable to find git the directory."));
        }
    }

    public static async Task WriteLocalSettingsJsonAsync(WorkerRuntime wr)
    {
        var content = await StaticResources.LocalSettingsJson;
        content = content.Replace($"{{{Constants.FunctionsWorkerRuntime}}}", WorkerRuntimeLanguageHelper.GetRuntimeMoniker(wr));

        var storage = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Constants.StorageEmulatorConnectionString : string.Empty;
        content = content.Replace($"{{{Constants.AzureWebJobsStorage}}}", storage);

        if (wr == WorkerRuntime.Powershell)
        {
            content = AddLocalSetting(content, Constants.FunctionsWorkerRuntimeVersion, Constants.PowerShellWorkerDefaultVersion);
        }

        await FileSystemHelpers.WriteFileIfNotExists("local.settings.json", content);

        static string AddLocalSetting(string json, string key, string value)
        {
            var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json);
            if (obj.TryGetValue("Values", StringComparison.OrdinalIgnoreCase, out var valuesContent) && valuesContent is Newtonsoft.Json.Linq.JObject values)
            {
                values.Property(Constants.FunctionsWorkerRuntime).AddAfterSelf(new Newtonsoft.Json.Linq.JProperty(key, value));
            }

            return Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
        }
    }
}
