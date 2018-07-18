using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.ActionsTests.Unit
{
    public class LoginActionTests : ActionTestsBase
    {
        public LoginActionTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task Defaults_To_Interactive_Login()
        {
            var armManagerMock = new Mock<IArmManager>();
            var armTokenManager = new Mock<IArmTokenManager>();
            var settingsMock = new Mock<ISettings>();
            var action = new LoginAction(armManagerMock.Object, settingsMock.Object, armTokenManager.Object);
            var args = new string[0];

            // Test
            action.ParseArgs(args);
            await action.RunAsync();

            // Assert
            armTokenManager.Verify(m => m.Login(), Times.Once);
        }

        [Fact]
        public async Task Can_Login_With_Username_And_Password()
        {
            var armManagerMock = new Mock<IArmManager>();
            var armTokenManager = new Mock<IArmTokenManager>();
            var settingsMock = new Mock<ISettings>();
            var action = new LoginAction(armManagerMock.Object, settingsMock.Object, armTokenManager.Object);
            var username = Guid.NewGuid().ToString();
            var password = Guid.NewGuid().ToString();
            var args = new[] { "-u", username, "-w", password };

            // Test
            action.ParseArgs(args);
            await action.RunAsync();
        }
    }
}
