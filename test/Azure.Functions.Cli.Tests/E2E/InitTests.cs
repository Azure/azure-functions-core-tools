using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
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

        [Theory]
        [InlineData("dotnet")]
        [InlineData("dotnet-isolated")]
        [InlineData("powershell")]
        public Task init_with_only_runtime_option(string workerRuntime)
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
                Commands = new[] { $"init --{workerRuntime}" },
                CheckFiles = files.ToArray(),
                OutputContains = new[]
                {
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                },
                OutputDoesntContain = new[] { "Initialized empty Git repository" }
            }, _output);
        }

        [Theory]
        [InlineData("node", "v3")]
        [InlineData("node", "v4")]
        [InlineData("node", "")]
        [InlineData("java", "v1")]
        [InlineData("python", "v1")]
        [InlineData("python", "v2")]
        [InlineData("python", "")]
        public Task init_with_worker_runtime_and_model(string workerRuntime, string programmingModel)
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
                }
            };

            if (workerRuntime == "python" && (programmingModel == "v2" || programmingModel == string.Empty))
            {
                files.Add(new FileResult
                {
                    Name = "function_app.py",
                });
            }
            else if (workerRuntime == "node" && (programmingModel == "v4" || programmingModel == string.Empty))
            {
                files.Add(new FileResult
                {
                    Name = "package.json",
                    ContentContains = new[]
                        {
                            "\"@azure/functions\": \"^4"
                        }
                });
            }

            var programmingModelFlag = programmingModel == string.Empty ? string.Empty : $"--model {programmingModel}";

            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime {workerRuntime} {programmingModelFlag}" },
                CheckFiles = files.ToArray(),
                OutputContains = new[]
                {
                    "Writing .gitignore",
                    "Writing host.json",
                    "Writing local.settings.json",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                },
                OutputDoesntContain = new[] { "Initialized empty Git repository" },
                CommandTimeout = TimeSpan.FromSeconds(120)
            }, _output);
        }

        [Theory]
        [InlineData("node", "v1")]
        [InlineData("node", "v2")]
        [InlineData("java", "v2")]
        [InlineData("python", "v3")]
        public Task init_with_worker_runtime_and_unsupported_model(string workerRuntime, string programmingModel)
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime {workerRuntime} --model {programmingModel}" },
                HasStandardError = true,
                ErrorContains = new[]
                {
                    $"programming model is not supported for worker runtime {workerRuntime}. Supported programming models for worker runtime {workerRuntime} are:"
                }
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
                            "v4"
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
        [InlineData("dotnet", "4")]
        [InlineData("node", "4")]
        [InlineData("powershell", "4")]
        public Task init_with_Dockerfile(string workerRuntime, string version)
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime {workerRuntime} --docker" },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "Dockerfile",
                        ContentContains = new[] { $"FROM mcr.microsoft.com/azure-functions/{workerRuntime}:{version}" }
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(120),
                OutputContains = new[] { "Dockerfile" }
            }, _output);
        }

        [SkippableFact]
        public async Task init_with_python_Dockerfile()
        {
            WorkerLanguageVersionInfo worker = await PythonHelpers.GetEnvironmentPythonVersion();
            Skip.If(worker == null);

            await CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime python --docker" },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "Dockerfile",
                        ContentContains = new[] { $"FROM mcr.microsoft.com/azure-functions/python:4-python{worker.Major}.{worker.Minor}" }
                    }
                },
                OutputContains = new[] { "Dockerfile" }
            }, _output);
        }

        [Fact]
        public Task init_with_dotnetIsolated_dockerfile()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime dotnet-isolated --docker" },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "Dockerfile",
                        ContentContains = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated6.0" }
                    }
                },
                OutputContains = new[] { "Dockerfile" }
            }, _output);
        }

        [Fact]
        public Task init_with_dotnet7Isolated_dockerfile()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime dotnet-isolated --target-framework net7.0 --docker" },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "Dockerfile",
                        ContentContains = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated7.0" }
                    }
                },
                OutputContains = new[] { "Dockerfile" }
            }, _output);
        }

        [Fact]
        public Task init_with_Dockerfile_for_csx()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . --worker-runtime dotnet --docker --csx" },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "Dockerfile",
                        ContentNotContains = new[] { "dotnet publish" },
                        ContentContains = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" }
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

        [Theory]
        [InlineData("--worker-runtime node --language typescript")]
        [InlineData("--typescript")]
        public Task init_ts_app_using_lang(string initCommand)
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { $"init . {initCommand} --docker" },
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
                    },
                    new FileResult
                    {
                        Name = "Dockerfile",
                        ContentContains = new []
                        {
                            "mcr.microsoft.com/azure-functions/node:4",
                            "npm run build"
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
                    "Writing Dockerfile",
                    "Writing .dockerignore"
                },
                CommandTimeout = TimeSpan.FromSeconds(120)
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
                },
                CommandTimeout = TimeSpan.FromSeconds(120)
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
                },
                CommandTimeout = TimeSpan.FromSeconds(240)
            }, _output);
        }

        [Theory]
        [InlineData("dotnet", "4")]
        [InlineData("node", "4")]
        [InlineData("powershell", "4")]
        public Task init_docker_only_for_existing_project(string workerRuntime, string version)
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
                        ContentContains = new[] { $"FROM mcr.microsoft.com/azure-functions/{workerRuntime}:{version}" }
                    }
                },
                OutputContains = new[] { "Dockerfile" },
                CommandTimeout = TimeSpan.FromSeconds(120)
            }, _output);
        }

        [Fact]
        public Task init_docker_only_for_csx_project()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"init . --worker-runtime dotnet --csx",
                    $"init . --docker-only --csx",
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "Dockerfile",
                        ContentNotContains = new[] { "dotnet publish" },
                        ContentContains = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" }
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
        public Task init_function_app_powershell_enable_managed_dependencies_and_set_default_version()
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
                            "logging",
                            "applicationInsights",
                            "extensionBundle",
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
                            "For latest supported version, go to 'https://www.powershellgallery.com/packages/Az'.",
                            "To use the Az module in your function app, please uncomment the line below.",
                            "Az",
                        }
                    },
                    new FileResult
                    {
                        Name = "profile.ps1",
                        ContentContains = new []
                        {
                            "env:MSI_SECRET",
                            "Disable-AzContextAutosave -Scope Process | Out-Null",
                            "Connect-AzAccount -Identity"
                        }
                    },
                    new FileResult
                    {
                        Name = "local.settings.json",
                        ContentContains = new []
                        {
                            "FUNCTIONS_WORKER_RUNTIME",
                            "powershell",
                            "FUNCTIONS_WORKER_RUNTIME_VERSION",
                            "7.2"
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
        public Task init_function_app_contains_logging_config()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] { "init . --worker-runtime node" },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = "host.json",
                        ContentContains = new []
                        {
                            "applicationInsights",
                            "excludedTypes",
                            "Request",
                            "logging"
                        }
                    }
                },
                OutputContains = new[]
                {
                    "Writing host.json"
                },
                CommandTimeout = TimeSpan.FromSeconds(120)
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
                    "init \"anapp\" --worker-runtime python -m v1",
                    "init \"anapp\" --worker-runtime python -m v1"
                },
                OutputContains = new[]
                {
                    "Writing .gitignore",
                    "Writing host.json",
                    "Writing local.settings.json",
                    "Writing getting_started.md",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json",
                    "requirements.txt already exists. Skipped!",
                    ".gitignore already exists. Skipped!",
                    "host.json already exists. Skipped!",
                    "getting_started.md already exists. Skipped!",
                    "local.settings.json already exists. Skipped!",
                    $".vscode{Path.DirectorySeparatorChar}extensions.json already exists. Skipped!"
                }
            }, _output);
        }

        [Fact]
        public Task init_python_app_twice_new_programming_model()
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

        [Theory]
        [InlineData("-m V1")]
        [InlineData("-m V2")]
        [InlineData("")]
        public Task init_python_app_generates_requirements_txt(string modelParameter)
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    $"init . --worker-runtime python {modelParameter}"
                },
                OutputContains = new[]
                {
                    "Writing requirements.txt"
                },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = "requirements.txt",
                        ContentContains = new []
                        {
                            "# Do not include azure-functions-worker",
                            "azure-functions"
                        }
                    }
                },
            }, _output);
        }

        [Fact]
        public Task init_python_app_generates_getting_started_md()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime python -m V1"
                },
                OutputContains = new[]
                {
                    "Writing getting_started.md"
                },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = "getting_started.md",
                        ContentContains = new []
                        {
                            "## Getting Started with Azure Function"
                        }
                    }
                },
            }, _output);
        }
    }
}
