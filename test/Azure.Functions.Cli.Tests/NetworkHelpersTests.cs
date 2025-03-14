using System;
using System.Net.Sockets;
using Xunit;
using Azure.Functions.Cli.Helpers;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
  public class NetworkHelpersTests : BaseE2ETest
  {
    public NetworkHelpersTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void IsPortAvailable_PortIsAvailable_ReturnsTrue()
    {
      int port = NetworkHelpers.GetAvailablePort();
      bool result = NetworkHelpers.IsPortAvailable(port);
      Assert.True(result);
    }

    [Fact]
    public void IsPortAvailable_PortIsNotAvailable_ReturnsFalse()
    {
      int port = NetworkHelpers.GetAvailablePort();
      var listener = new TcpListener(System.Net.IPAddress.Any, port);
      listener.Start();

      bool result = NetworkHelpers.IsPortAvailable(port);
      listener.Stop();

      Assert.False(result);
    }

    [Fact]
    public void GetAvailablePort_ReturnsValidPort()
    {
      int port = NetworkHelpers.GetAvailablePort();
      Assert.InRange(port, 1024, 65535);
    }

    [Fact]
    public void GetNextAvailablePort_ValidStartPort_ReturnsNextAvailablePort()
    {
      int startPort = 5000;
      int port = NetworkHelpers.GetNextAvailablePort(startPort);
      Assert.InRange(port, startPort, 65535);
    }

    [Fact]
    public void GetNextAvailablePort_InvalidStartPort_ThrowsArgumentOutOfRangeException()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => NetworkHelpers.GetNextAvailablePort(1023));
      Assert.Throws<ArgumentOutOfRangeException>(() => NetworkHelpers.GetNextAvailablePort(65536));
    }
  }
}
