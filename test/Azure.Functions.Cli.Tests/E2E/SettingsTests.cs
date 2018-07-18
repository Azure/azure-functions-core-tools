using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class SettingsTests : BaseE2ETest
    {
        public SettingsTests(ITestOutputHelper output) : base(output) { }

        public Task add_setting_plain_text()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "func init . --worker-runtime node --no-source-control",
                    "func settings add testKey valueValue"
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "local.settings.json",
                        ContentContains = new[]
                        {
                            "\"IsEncrypted\": false",
                            "\"testKey\": \"valueValye\","
                        }
                    }
                }
            }, _output);
        }

        public Task add_setting_encrypted()
        {
            return CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "func init . --worker-runtime node --no-source-control",
                        "func settings encrypt",
                        "func settings add testKey valueValue"
                    },
                    CheckFiles = new[]
                    {
                        new FileResult
                        {
                            Name = "local.settings.json",
                            ContentContains = new[]
                            {
                                "\"IsEncrypted\": true",
                                "\"testKey\":"
                            },
                            ContentNotContains = new[]
                            {
                                "valueValue"
                            }
                        }
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "func settings decrypt"
                    },
                    CheckFiles = new[]
                    {
                        new FileResult
                        {
                            Name = "local.settings.json",
                            ContentContains = new[]
                            {
                                "\"IsEncrypted\": false",
                                "\"testKey\": \"valueValye\","
                            }
                        }
                    }
                }
            }, _output);
        }
    }
}