using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class CreateFunctionTests : BaseE2ETest
    {
        public CreateFunctionTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task create_template_function_success_message()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet",
                    "new --template HttpTrigger --name testfunc"
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"HttpTrigger\" template."
                }
            }, _output);
        }

        [Fact]
        public async Task create_timerTrigger_authConfigured_returns_error()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template TimerTrigger --name testfunc --authlevel function"
                },
                HasStandardError = true,
                ErrorContains = new[]
                {
                    Constants.AuthLevelErrorMessage
                }
            }, _output);
        }

        [Fact]
        public async Task create_httpTrigger_Invalid_AuthConfig_returns_error()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template httpTrigger --name testfunc --authlevel invalid"
                },
                OutputContains = new[]
                {
                    "Authorization level is applicable to templates that use Http trigger, Allowed values: [function, anonymous, admin]. Authorization level is not enforced when running functions from core tools"
                }
            }, _output);
        }

        [Fact]
        public async Task create_httpTrigger_with_authConfigured_node()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template HttpTrigger --name testfunc --authlevel function"
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"HttpTrigger\" template."
                }
            }, _output);
        }

        [Fact]
        public async Task create_httpTrigger_with_authConfigured_dotnet()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet",
                    "new --template HttpTrigger --name testfunc --authlevel function"
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"HttpTrigger\" template."
                }
            }, _output);
        }

        [Fact]
        public async Task create_template_function_sanitization_dotnet()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init 12n.e.0w-file$ --worker-runtime dotnet",
                    "new --prefix 12n.e.0w-file$ --template HttpTrigger --name 12@n.other-file$"
                },
                CommandTimeout = new TimeSpan(0, 1, 0),
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "12n.e.0w-file$/_12_n_other_file_.cs",
                        ContentContains = new[]
                        {
                            "namespace _12n.e__w_file_",
                            "public static class _12_n_other_file_"
                        }
                    }
                }
            }, _output);
        }

        [Fact]
        public async Task create_template_function_using_alias()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --language js --template \"http trigger\" --name testfunc"
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"http trigger\" template."
                }
            }, _output);
        }

        [Fact]
        public async Task create_function_custom()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime custom --no-bundle",
                    "new --template \"Http Trigger\" --name testfunc"
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"Http Trigger\" template."
                }
            }, _output);
        }

        [Fact]
        public async Task create_template_function_js_no_space_name()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --language js --template httptrigger --name testfunc"
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"httptrigger\" template."
                }
            }, _output);
        }

        [Fact]
        public async Task create_template_function_dotnet_space_name()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet",
                    "new --template \"http trigger\" --name testfunc2"
                },
                OutputContains = new[]
                {
                    "The function \"testfunc2\" was created successfully from the \"http trigger\" template."
                }
            }, _output);
        }

        [Fact]
        public async Task create_typescript_template()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node --language typescript",
                    "new --template \"http trigger\" --name testfunc"
                },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = Path.Combine("testfunc", "function.json"),
                        ContentContains = new []
                        {
                            "../dist/testfunc/index.js",
                            "authLevel",
                            "methods",
                            "httpTrigger"
                        }
                    }
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"http trigger\" template."
                }
            }, _output);
        }

        [Fact]
        public async Task create_typescript_template_blob()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node --language typescript",
                    "new --template \"azure Blob Storage trigger\" --name testfunc"
                },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = Path.Combine("testfunc", "function.json"),
                        ContentContains = new []
                        {
                            "../dist/testfunc/index.js",
                            "blobTrigger"
                        },
                        ContentNotContains = new []
                        {
                            "authLevel",
                            "methods"
                        }
                    }
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"azure Blob Storage trigger\" template."
                }
            }, _output);
        }

        [Fact]
        public async Task create_function_no_init_csx()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "new --csx --template \"http trigger\" --name testfunc"
                },
                CheckFiles = new FileResult[]
                {
                    new FileResult
                    {
                        Name = Path.Combine("testfunc", "function.json"),
                        ContentContains = new []
                        {
                            "httpTrigger"
                        }
                    },
                    new FileResult
                    {
                        Name = Path.Combine("local.settings.json"),
                        ContentContains = new []
                        {
                            "\"FUNCTIONS_WORKER_RUNTIME\": \"dotnet\""
                        }
                    }
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"http trigger\" template."
                }
            }, _output);
        }
    }
}
