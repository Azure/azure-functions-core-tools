// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class GolangHelpers
    {
        private static readonly string _mainGoTemplate = @"package main

import (
	""fmt""
	""net/http""

	""github.com/azure/azure-functions-golang-worker/sdk""
	""github.com/azure/azure-functions-golang-worker/sdk/bindings""
)

func main() {
	app := sdk.FunctionApp()
	app.HTTP(""hello"", hello).Methods(""GET"", ""POST"").Auth(""anonymous"")
	app.Start()
}

func hello(w http.ResponseWriter, r *http.Request) {
	name := r.URL.Query().Get(""name"")
	if name == """" {
		name = ""world""
	}
	fmt.Fprintf(w, ""Hello, %s!"", name)
}
";

        public static string GetExecutableName()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "app.exe" : "app";
        }

        public static async Task SetupGolangProject()
        {
            if (!CommandChecker.CommandExists("go"))
            {
                throw new CliException("Go is required but not found on PATH. Please install Go from https://go.dev/dl/");
            }

            // Write main.go
            await FileSystemHelpers.WriteFileIfNotExists("main.go", _mainGoTemplate);
            ColoredConsole.WriteLine(AdditionalInfoColor("Created main.go"));

            // Run go mod init
            var folderName = Path.GetFileName(Environment.CurrentDirectory);
            var exe = new Executable("go", $"mod init {folderName}");
            var exitCode = await exe.RunAsync(o => ColoredConsole.WriteLine(o), e => ColoredConsole.Error.WriteLine(e));
            if (exitCode != 0)
            {
                ColoredConsole.WriteLine(WarningColor("Failed to run 'go mod init'. You may need to run it manually."));
            }

            // Run go mod tidy to fetch dependencies
            var tidy = new Executable("go", "mod tidy");
            exitCode = await tidy.RunAsync(o => ColoredConsole.WriteLine(o), e => ColoredConsole.Error.WriteLine(e));
            if (exitCode != 0)
            {
                ColoredConsole.WriteLine(WarningColor("Failed to run 'go mod tidy'. You may need to run it manually."));
            }
        }

        public static async Task BuildGoProject()
        {
            if (!CommandChecker.CommandExists("go"))
            {
                throw new CliException("Go is required but not found on PATH. Please install Go from https://go.dev/dl/");
            }

            var executableName = GetExecutableName();
            ColoredConsole.WriteLine($"Building Go project to '{executableName}'...");

            var exe = new Executable("go", $"build -o {executableName} .");
            var exitCode = await exe.RunAsync(o => ColoredConsole.WriteLine(o), e => ColoredConsole.Error.WriteLine(e));
            if (exitCode != 0)
            {
                throw new CliException("Error building Go project. Please ensure your Go code compiles successfully.");
            }
        }
    }
}
