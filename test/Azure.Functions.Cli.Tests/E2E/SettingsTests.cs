using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Xunit;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class SettingsTests
    {
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
                            "\"IsEncrypted\": false",
                            "\"testKey\": \"valueValue\""
                        }
                    }
                }
            });
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
                        "settings decrypt"
                    },
                    CheckFiles = new[]
                    {
                        new FileResult
                        {
                            Name = "local.settings.json",
                            ContentContains = new[]
                            {
                                "\"IsEncrypted\": false",
                                "\"testKey\": \"valueValue\""
                            }
                        }
                    }
                }
            });
        }
    }
}