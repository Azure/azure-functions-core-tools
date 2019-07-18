﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class InitTests : BaseE2ETest
    {
        public InitTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData("node")]
        [InlineData("java")]
        [InlineData("python")]
        public Task init_with_worker_runtime(string workerRuntime)
        {
            var files = new List<FileResult>
            {
                new FileResult
                {
                    Name = "local.settings.json",
                    ContentContains = new []
                    {
                        "FUNCTIONS_WORKER_RUNTIME",
                        workerRuntime
                    }
                },

            };

            if (workerRuntime == "powershell")
            {
                files.Add(new FileResult
                {
                    Name = "profile.ps1",
                    ContentContains = new[] { "# Azure Functions profile.ps1" }
                });
            }

            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime {workerRuntime}" },
                CheckFiles = files.ToArray(),
                OutputContains = new[]
                {
                    "Writing .gitignore",
                    "Writing host.json",
                    "Writing local.settings.json",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                },
                OutputDoesntContain = new[] { "Initialized empty Git repository" }
            }, _output);
        }

        [Fact]
        public Task init_dotnet_app()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { "init dotnet-funcs --worker-runtime dotnet" },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = Path.Combine("dotnet-funcs", "local.settings.json"),
                        ContentContains = new[]
                        {
                            "FUNCTIONS_WORKER_RUNTIME",
                            "dotnet"
                        }
                    },
                    new FileResult
                    {
                        Name = Path.Combine("dotnet-funcs", "dotnet-funcs.csproj"),
                        ContentContains = new[]
                        {
                            "Microsoft.NET.Sdk.Functions",
                            "v2"
                        }
                    }
                }
            }, _output);
        }

        [Fact]
        public Task init_with_unknown_worker_runtime()
        {
            const string unknownWorkerRuntime = "foo";
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime {unknownWorkerRuntime}" },
                HasStandardError = true,
                ErrorContains = new[]
                {
                    $"Worker runtime '{unknownWorkerRuntime}' is not a valid option."
                }
            }, _output);
        }

        [Fact]
        public Task init_with_no_source_control()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { "init . --worker-runtime node" },
                CheckDirectories = new[]
                {
                    new DirectoryResult { Name = ".git", Exists = false }
                },
            }, _output);
        }

        [Theory]
        [InlineData("dotnet")]
        [InlineData("node")]
        public Task init_with_Dockerfile(string workerRuntime)
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime {workerRuntime} --docker" },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "Dockerfile",
                        ContentContains = new[] { $"FROM mcr.microsoft.com/azure-functions/{workerRuntime}:2.0" }
                    }
                },
                OutputContains = new[] { "Dockerfile" }
            }, _output);
        }

        [Fact]
        public Task init_csx_app()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { "init . --worker-runtime dotnet --csx" },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = "local.settings.json",
                        ContentContains = new []
                        {
                            "FUNCTIONS_WORKER_RUNTIME",
                            "dotnet"
                        }
                    }
                },
                OutputContains = new[]
                {
                    "Writing .gitignore",
                    "Writing host.json",
                    "Writing local.settings.json",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                }
            }, _output);
        }

        [Fact]
        public Task init_app_with_spaces()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init \"functions project\" --worker-runtime dotnet",
                    "new --prefix \"functions project\" --template BlobTrigger --name testfunc"
                },
                CommandTimeout = new TimeSpan(0, 1, 0)
            });
        }

        [Fact]
        public Task init_ts_app_using_lang()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { "init . --worker-runtime node --language typescript" },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = "local.settings.json",
                        ContentContains = new []
                        {
                            "FUNCTIONS_WORKER_RUNTIME",
                            "node"
                        }
                    }
                },
                OutputContains = new[]
                {
                    "Writing tsconfig.json",
                    "Writing .funcignore",
                    "Writing package.json",
                    "Writing .gitignore",
                    "Writing host.json",
                    "Writing local.settings.json",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                }
            }, _output);
        }

                [Fact]
        public Task javascript_adds_packagejson()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { "init . --worker-runtime node" },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = "local.settings.json",
                        ContentContains = new []
                        {
                            "FUNCTIONS_WORKER_RUNTIME",
                            "node"
                        }
                    }
                },
                OutputContains = new[]
                {
                    "Writing package.json",
                    "Writing .gitignore",
                    "Writing host.json",
                    "Writing local.settings.json",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                }
            }, _output);
        }

        [Fact]
        public Task init_ts_app_using_runtime()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { "init . --worker-runtime typescript" },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = "local.settings.json",
                        ContentContains = new []
                        {
                            "FUNCTIONS_WORKER_RUNTIME",
                            "node"
                        }
                    }
                },
                OutputContains = new[]
                {
                    "Writing tsconfig.json",
                    "Writing .funcignore",
                    "Writing package.json",
                    "Writing .gitignore",
                    "Writing host.json",
                    "Writing local.settings.json",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                }
            }, _output);
        }

        [Theory]
        [InlineData("dotnet")]
        [InlineData("node")]
        public Task init_docker_only_for_existing_project(string workerRuntime)
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"init . --worker-runtime {workerRuntime}",
                    $"init . --docker-only",
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "Dockerfile",
                        ContentContains = new[] { $"FROM mcr.microsoft.com/azure-functions/{workerRuntime}:2.0" }
                    }
                },
                OutputContains = new[] { "Dockerfile" }
            }, _output);
        }

        [Fact]
        public Task init_docker_only_no_project()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"init . --docker-only"
                },
                HasStandardError = true,
                ErrorContains = new[]
                {
                    $"Can't determine project language from files."
                }
            }, _output);
        }

        [Fact]
        public Task init_function_app_powershell_supports_managed_dependencies()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { "init . --worker-runtime powershell --managed-dependencies" },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = "host.json",
                        ContentContains = new []
                        {
                            "managedDependency",
                            "enabled",
                            "true"
                        }
                    },
                    new FileResult
                    {
                        Name = "requirements.psd1",
                        ContentContains = new []
                        {
                            "Az",
                        }
                    }
                },
                OutputContains = new[]
                {
                    "Writing profile.ps1",
                    "Writing requirements.psd1",
                    "Writing .gitignore",
                    "Writing host.json",
                    "Writing local.settings.json",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                }
            }, _output);
        }

        [Fact]
        public Task init_managed_dependencies_is_only_supported_in_powershell()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"init . --worker-runtime python --managed-dependencies "
                },
                HasStandardError = true,
                ErrorContains = new[]
                {
                    $"Managed dependencies is only supported for PowerShell"
                }
            }, _output);
        }

        [Fact]
        public Task init_python_app_twice()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init \"anapp\" --worker-runtime python",
                    "init \"anapp\" --worker-runtime python"
                },
                OutputContains = new[]
                {
                    "Writing .gitignore",
                    "Writing host.json",
                    "Writing local.settings.json",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                    "requirements.txt already exists. Skipped!",
                    ".gitignore already exists. Skipped!",
                    "host.json already exists. Skipped!",
                    "local.settings.json already exists. Skipped!",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json already exists. Skipped!"
                }
            }, _output);
        }
    }
}
