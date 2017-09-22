using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class LoginActionTests : ActionTestsBase
    {
        public LoginActionTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task Defaults_To_Interactive_Login()
        {
            var armManagerMock = new Mock<IArmManager>();
            var settingsMock = new Mock<ISettings>();
            var action = new LoginAction(armManagerMock.Object, settingsMock.Object);
            var args = new string[0];

            // Test
            action.ParseArgs(args);
            await action.RunAsync();

            // Assert
            armManagerMock.Verify(m => m.LoginAsync(), Times.Once);

            // cleanup
            CleanUp();
        }

        [Fact]
        public async Task Can_Login_With_Username_And_Password()
        {
            var armManagerMock = new Mock<IArmManager>();
            var settingsMock = new Mock<ISettings>();
            var action = new LoginAction(armManagerMock.Object, settingsMock.Object);
            var username = Guid.NewGuid().ToString();
            var password = Guid.NewGuid().ToString();
            var args = new[] { "-u", username, "-w", password };

            // Test
            action.ParseArgs(args);
            await action.RunAsync();

            // Assert
            armManagerMock.Verify(m => m.LoginAsync(
                It.Is<string>(s => s == username),
                It.Is<string>(s => s == password)
            ), Times.Once);

            // cleanup
            CleanUp();
        }
    }
}