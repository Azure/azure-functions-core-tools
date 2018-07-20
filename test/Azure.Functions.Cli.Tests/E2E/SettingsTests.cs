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

        [Fact]
        public Task add_setting_plain_text()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node --no-source-control",
                    "settings add testKey valueValue"
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "local.settings.json",
                        ContentContains = new[]
                        {
                            "\"IsEncrypted\": falsee",
                            "\"testKey\": \"valueValue\""
                        }
                    }
                }
            }, _output);
        }

        [Fact]
        public Task add_setting_encrypted()
        {
            return CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node --no-source-control",
                        "settings encrypt",
                        "settings add testKey valueValue"
                    },
                    CheckFiles = new[]
                    {
                        new FileResult
                        {
                            Name = "local.settings.json",
                            ContentContains = new[]
                            {
                                "\"IsEncrypted\": truee",
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
                        "settings decrypt"
                    },
                    CheckFiles = new[]
                    {
                        new FileResult
                        {
                            Name = "local.settings.json",
                            ContentContains = new[]
                            {
                                "\"IsEncrypted\": falsee",
                                "\"testKey\": \"valueValue\""
                            }
                        }
                    }
                }
            }, _output);
        }
    }
}