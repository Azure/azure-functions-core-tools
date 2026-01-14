// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Arm.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class PublishFunctionAppActionTests
    {
        [Fact]
        public async Task UpdateRuntimeConfigForFlex_SkipsUpdate_WhenNoRuntimeVersionDetected()
        {
            // Arrange
            var site = new Site("test-site")
            {
                Location = "eastus",
                FunctionAppConfig = new FunctionAppConfig
                {
                    Runtime = new Runtime
                    {
                        Name = "dotnet-isolated",
                        Version = "9.0"
                    }
                }
            };
            
            var helperServiceMock = new Mock<PublishFunctionAppAction.AzureHelperService>(null, null);
            
            // Act - passing null for runtimeVersion should skip the update
            await PublishFunctionAppAction.UpdateRuntimeConfigForFlex(
                site, 
                "dotnet-isolated", 
                null, // No version detected
                helperServiceMock.Object,
                force: false,
                overwriteSettings: false
            );
            
            // Assert - UpdateFlexRuntime should not be called
            helperServiceMock.Verify(
                x => x.UpdateFlexRuntime(It.IsAny<Site>(), It.IsAny<string>(), It.IsAny<string>()), 
                Times.Never
            );
        }

        [Fact]
        public async Task UpdateRuntimeConfigForFlex_SkipsUpdate_WhenVersionsMatch()
        {
            // Arrange
            var site = new Site("test-site")
            {
                Location = "eastus",
                FunctionAppConfig = new FunctionAppConfig
                {
                    Runtime = new Runtime
                    {
                        Name = "dotnet-isolated",
                        Version = "9.0"
                    }
                }
            };
            
            var helperServiceMock = new Mock<PublishFunctionAppAction.AzureHelperService>(null, null);
            helperServiceMock
                .Setup(x => x.GetFlexFunctionsStacks(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(CreateMockFlexStacks());
            
            // Act - passing same version should skip the update
            await PublishFunctionAppAction.UpdateRuntimeConfigForFlex(
                site, 
                "dotnet-isolated", 
                "9.0", // Same version as Azure
                helperServiceMock.Object,
                force: false,
                overwriteSettings: false
            );
            
            // Assert - UpdateFlexRuntime should not be called
            helperServiceMock.Verify(
                x => x.UpdateFlexRuntime(It.IsAny<Site>(), It.IsAny<string>(), It.IsAny<string>()), 
                Times.Never
            );
        }

        [Fact]
        public async Task UpdateRuntimeConfigForFlex_UpdatesRuntime_WhenVersionsDifferAndForceIsTrue()
        {
            // Arrange
            var site = new Site("test-site")
            {
                Location = "eastus",
                FunctionAppConfig = new FunctionAppConfig
                {
                    Runtime = new Runtime
                    {
                        Name = "dotnet-isolated",
                        Version = "8.0"
                    }
                }
            };
            
            var helperServiceMock = new Mock<PublishFunctionAppAction.AzureHelperService>(null, null);
            helperServiceMock
                .Setup(x => x.GetFlexFunctionsStacks(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(CreateMockFlexStacks());
            
            // Act - different version with force should update
            await PublishFunctionAppAction.UpdateRuntimeConfigForFlex(
                site, 
                "dotnet-isolated", 
                "9.0", // Different version from Azure (8.0)
                helperServiceMock.Object,
                force: true, // Force the update
                overwriteSettings: false
            );
            
            // Assert - UpdateFlexRuntime should be called
            helperServiceMock.Verify(
                x => x.UpdateFlexRuntime(site, "dotnet-isolated", "9.0"), 
                Times.Once
            );
        }

        private static FlexFunctionsStacks CreateMockFlexStacks()
        {
            return new FlexFunctionsStacks
            {
                Languages = new List<Language>
                {
                    new Language
                    {
                        LanguageProperties = new LanguageProperties
                        {
                            MajorVersions = new List<MajorVersion>
                            {
                                new MajorVersion
                                {
                                    MinorVersions = new List<MinorVersion>
                                    {
                                        new MinorVersion
                                        {
                                            StackSettings = new StackSettings
                                            {
                                                LinuxRuntimeSettings = new LinuxRuntimeSettings
                                                {
                                                    Sku = new List<FlexSku>
                                                    {
                                                        new FlexSku
                                                        {
                                                            SkuCode = "FC1",
                                                            FunctionAppConfigProperties = new FunctionAppConfig
                                                            {
                                                                Runtime = new Runtime
                                                                {
                                                                    Name = "dotnet-isolated",
                                                                    Version = "9.0"
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
