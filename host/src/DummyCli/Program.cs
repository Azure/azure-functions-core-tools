using System.Runtime.InteropServices;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine($"Hello from DummyCLI Main. Current OS:{RuntimeInformation.OSDescription}, FrameworkDescription:{RuntimeInformation.FrameworkDescription}");

        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(1000);
            Console.WriteLine($"DummyCLI is running for {i} seconds");
        }

        Console.WriteLine("DummyCLI is done");
    }
}