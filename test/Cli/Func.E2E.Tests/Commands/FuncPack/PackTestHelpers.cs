// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncPack
{
    /// <summary>
    /// Helper class providing extensible utilities for func pack testing.
    /// This class centralizes common pack testing patterns and makes the tests more maintainable.
    /// </summary>
    public static class PackTestHelpers
    {
        /// <summary>
        /// Runtime configurations for different Azure Functions runtime stacks.
        /// This makes it easy to add new runtimes or modify existing ones.
        /// </summary>
        public static readonly Dictionary<string, RuntimeConfig> SupportedRuntimes = new()
        {
            ["node"] = new RuntimeConfig
            {
                Runtime = "node",
                SupportsLocalBuild = true,
                SupportsRemoteBuild = true,
                DefaultTemplate = "HTTP Trigger",
                ExpectedFiles = new[] { "host.json", "package.json" },
                IgnoredFiles = new[] { "local.settings.json", "node_modules/" }
            },
            ["python"] = new RuntimeConfig
            {
                Runtime = "python",
                SupportsLocalBuild = true,
                SupportsRemoteBuild = true,
                SupportsNativeDeps = true,
                SupportsSquashfs = true,
                DefaultTemplate = "HTTP Trigger",
                ExpectedFiles = new[] { "host.json", "requirements.txt" },
                IgnoredFiles = new[] { "local.settings.json", ".venv/", "__pycache__/" }
            },
            ["powershell"] = new RuntimeConfig
            {
                Runtime = "powershell",
                SupportsLocalBuild = true,
                SupportsRemoteBuild = true,
                DefaultTemplate = "HTTP Trigger",
                ExpectedFiles = new[] { "host.json", "profile.ps1" },
                IgnoredFiles = new[] { "local.settings.json" }
            },
            ["java"] = new RuntimeConfig
            {
                Runtime = "java",
                SupportsLocalBuild = true,
                SupportsRemoteBuild = true,
                DefaultTemplate = "HTTP Trigger",
                ExpectedFiles = new[] { "host.json", "pom.xml" },
                IgnoredFiles = new[] { "local.settings.json", "target/" }
            },
            ["custom"] = new RuntimeConfig
            {
                Runtime = "custom",
                SupportsLocalBuild = true,
                SupportsRemoteBuild = true,
                DefaultTemplate = "HTTP Trigger",
                ExpectedFiles = new[] { "host.json" },
                IgnoredFiles = new[] { "local.settings.json" }
            },
            ["dotnet"] = new RuntimeConfig
            {
                Runtime = "dotnet",
                SupportsLocalBuild = false, // In-process .NET doesn't support local build pack
                SupportsRemoteBuild = true,
                DefaultTemplate = "HTTP Trigger",
                ExpectedFiles = new[] { "host.json" },
                IgnoredFiles = new[] { "local.settings.json", "bin/", "obj/" }
            },
            ["dotnet-isolated"] = new RuntimeConfig
            {
                Runtime = "dotnet-isolated",
                SupportsLocalBuild = true,
                SupportsRemoteBuild = true,
                DefaultTemplate = "HTTP Trigger",
                ExpectedFiles = new[] { "host.json" },
                IgnoredFiles = new[] { "local.settings.json", "bin/", "obj/" }
            }
        };

        /// <summary>
        /// Standard .funcignore patterns for testing ignore functionality.
        /// </summary>
        public static readonly Dictionary<string, string> StandardFuncIgnorePatterns = new()
        {
            ["basic"] = @"*.log
*.tmp
temp/
.git/
node_modules/
",
            ["python"] = @"__pycache__/
*.pyc
*.pyo
.pytest_cache/
.venv/
venv/
env/
.env
*.log
.git/
",
            ["node"] = @"node_modules/
*.log
.git/
.vscode/
*.js.map
dist/
",
            ["dotnet"] = @"bin/
obj/
*.user
*.suo
.vs/
.git/
*.log
",
            ["comprehensive"] = @"# Logs
*.log
logs/

# Temporary files
*.tmp
temp/
cache/

# Environment files
.env
.env.local

# IDE files
.vscode/
.vs/
*.user
*.suo

# Runtime specific
node_modules/
__pycache__/
bin/
obj/
target/

# Version control
.git/
.svn/

# OS files
.DS_Store
Thumbs.db
"
        };

        /// <summary>
        /// Creates a comprehensive function app setup for testing.
        /// </summary>
        public static FunctionAppSetup CreateFunctionApp(
            string funcPath,
            string workingDir,
            string runtime,
            string testName,
            ITestOutputHelper log,
            string template = null,
            string functionName = "httptrigger")
        {
            var config = SupportedRuntimes.GetValueOrDefault(runtime) 
                ?? throw new ArgumentException($"Unsupported runtime: {runtime}");

            template ??= config.DefaultTemplate;

            // Initialize function app
            var funcInitCommand = new FuncInitCommand(funcPath, testName, log);
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", runtime]);

            if (initResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to initialize {runtime} function app: {initResult.Error}");
            }

            // Create function
            var funcNewCommand = new FuncNewCommand(funcPath, testName, log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", $"\"{template}\"", "--name", functionName, "--authlevel", "anonymous"]);

            if (newResult.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to create function: {newResult.Error}");
            }

            return new FunctionAppSetup
            {
                Runtime = runtime,
                Config = config,
                WorkingDirectory = workingDir,
                FunctionName = functionName,
                Template = template,
                InitResult = initResult,
                NewResult = newResult
            };
        }

        /// <summary>
        /// Validates the contents of a pack output zip file.
        /// </summary>
        public static ZipValidationResult ValidatePackOutput(
            string zipPath,
            RuntimeConfig config,
            ITestOutputHelper log,
            string[] additionalExpectedFiles = null,
            string[] additionalIgnoredFiles = null)
        {
            if (!File.Exists(zipPath))
            {
                return new ZipValidationResult { IsValid = false, Error = $"Zip file not found at {zipPath}" };
            }

            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entries = archive.Entries.Select(e => e.FullName).ToArray();

                var expectedFiles = config.ExpectedFiles.Concat(additionalExpectedFiles ?? Array.Empty<string>()).ToArray();
                var ignoredFiles = config.IgnoredFiles.Concat(additionalIgnoredFiles ?? Array.Empty<string>()).ToArray();

                var result = new ZipValidationResult
                {
                    IsValid = true,
                    Entries = entries,
                    EntryCount = entries.Length
                };

                // Check for expected files
                foreach (var expectedFile in expectedFiles)
                {
                    if (!entries.Any(e => e.Contains(expectedFile)))
                    {
                        result.MissingFiles.Add(expectedFile);
                        result.IsValid = false;
                    }
                }

                // Check for ignored files (should not be present)
                foreach (var ignoredFile in ignoredFiles)
                {
                    if (entries.Any(e => e.Contains(ignoredFile)))
                    {
                        result.UnexpectedFiles.Add(ignoredFile);
                        result.IsValid = false;
                    }
                }

                // Log entries for debugging
                log.WriteLine($"Zip file contains {entries.Length} entries:");
                foreach (var entry in entries.Take(20)) // Log first 20 entries
                {
                    log.WriteLine($"  - {entry}");
                }
                if (entries.Length > 20)
                {
                    log.WriteLine($"  ... and {entries.Length - 20} more entries");
                }

                return result;
            }
            catch (Exception ex)
            {
                return new ZipValidationResult
                {
                    IsValid = false,
                    Error = $"Failed to read zip file: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Creates test files and directories for ignore testing.
        /// </summary>
        public static void CreateTestFilesForIgnoreTesting(string workingDir, string ignorePattern = "basic")
        {
            var testFiles = new Dictionary<string, string>
            {
                ["test-file.txt"] = "This should be included",
                ["important.json"] = "{}",
                ["README.md"] = "# Test Function App",
                ["debug.log"] = "Debug log content",
                ["cache.tmp"] = "Temporary cache",
                ["temp/data.txt"] = "Temporary data",
                ["logs/app.log"] = "Application logs",
                ["node_modules/package/index.js"] = "// Package content",
                ["__pycache__/module.pyc"] = "Compiled Python",
                ["bin/app.exe"] = "Binary file",
                ["obj/temp.obj"] = "Object file",
                [".git/config"] = "Git config",
                [".vscode/settings.json"] = "VS Code settings"
            };

            foreach (var (filePath, content) in testFiles)
            {
                var fullPath = Path.Combine(workingDir, filePath);
                var directory = Path.GetDirectoryName(fullPath);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                File.WriteAllText(fullPath, content);
            }
        }

        /// <summary>
        /// Gets the expected zip file path for a given working directory and output options.
        /// </summary>
        public static string GetExpectedZipPath(string workingDir, string outputPath = null, bool isSquashfs = false)
        {
            if (!string.IsNullOrEmpty(outputPath))
            {
                return Path.IsPathRooted(outputPath) 
                    ? outputPath 
                    : Path.Combine(workingDir, outputPath);
            }

            var extension = isSquashfs ? ".squashfs" : ".zip";
            return Path.Combine(workingDir, $"{Path.GetFileName(workingDir)}{extension}");
        }
    }

    /// <summary>
    /// Configuration for a specific runtime.
    /// </summary>
    public class RuntimeConfig
    {
        public string Runtime { get; set; } = string.Empty;
        public bool SupportsLocalBuild { get; set; } = true;
        public bool SupportsRemoteBuild { get; set; } = true;
        public bool SupportsNativeDeps { get; set; } = false;
        public bool SupportsSquashfs { get; set; } = false;
        public string DefaultTemplate { get; set; } = "HTTP Trigger";
        public string[] ExpectedFiles { get; set; } = Array.Empty<string>();
        public string[] IgnoredFiles { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Result of setting up a function app for testing.
    /// </summary>
    public class FunctionAppSetup
    {
        public string Runtime { get; set; } = string.Empty;
        public RuntimeConfig Config { get; set; } = new();
        public string WorkingDirectory { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public dynamic InitResult { get; set; } = null!;
        public dynamic NewResult { get; set; } = null!;
    }

    /// <summary>
    /// Result of validating a zip file's contents.
    /// </summary>
    public class ZipValidationResult
    {
        public bool IsValid { get; set; }
        public string Error { get; set; } = string.Empty;
        public string[] Entries { get; set; } = Array.Empty<string>();
        public int EntryCount { get; set; }
        public List<string> MissingFiles { get; set; } = new();
        public List<string> UnexpectedFiles { get; set; } = new();
    }
}