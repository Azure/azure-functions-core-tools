using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.NativeMethods;
using Colors.Net;
using static Colors.Net.StringStaticMethods;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    internal static class SecurityHelpers
    {
        private static bool CheckCurrentUser(dynamic obj)
        {
            if (obj != null && HasProperty(obj, "User"))
            {
                return $"{Environment.UserDomainName}\\{Environment.UserName}".Equals(obj.User.User.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static bool HasProperty(dynamic obj, string propertyName)
        {
            var dictionary = obj as IDictionary<string, object>;
            return dictionary != null && dictionary.ContainsKey(propertyName);
        }

        public static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static string ReadPassword()
        {
            var password = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ConsoleNativeMethods.ReadPassword()
                : InternalReadPassword();
            System.Console.WriteLine();
            return password;
        }

        // https://stackoverflow.com/q/3404421/3234163
        private static string InternalReadPassword()
        {
            var password = new StringBuilder();
            while(true)
            {
                var key = Console.ReadKey(true);
                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    password.Append(key.KeyChar);
                    ColoredConsole.Write("*");
                }
                else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    ColoredConsole.Write("\b \b");
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    return password.ToString();
                }
            }
        }

        internal static async Task<(X509Certificate2 cert, string path, string password)> GetOrCreateCertificate(string certPath, string certPassword)
        {
            if (!string.IsNullOrEmpty(certPath) && !string.IsNullOrEmpty(certPassword))
            {
                certPassword = File.Exists(certPassword)
                    ? File.ReadAllText(certPassword).Trim()
                    : certPassword;
                return (new X509Certificate2(certPath, certPassword), certPath, certPassword);
            }
            else if (CommandChecker.CommandExists("openssl"))
            {
                return await CreateCertificateOpenSSL();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ColoredConsole
                .WriteLine("Auto cert generation is currently not working on the .NET Core build.")
                .WriteLine("On Windows you can run:")
                .WriteLine()
                .Write(DarkCyan("PS> "))
                .WriteLine($"$cert = {Yellow("New-SelfSignedCertificate")} -Subject localhost -DnsName localhost -FriendlyName \"Functions Development\" -KeyUsage DigitalSignature -TextExtension @(\"2.5.29.37={{text}}1.3.6.1.5.5.7.3.1\")")
                .Write(DarkCyan("PS> "))
                .WriteLine($"{Yellow("Export-PfxCertificate")} -Cert $cert -FilePath certificate.pfx -Password (ConvertTo-SecureString -String {Red("<password>")} -Force -AsPlainText)")
                .WriteLine()
                .WriteLine("For more checkout https://docs.microsoft.com/en-us/aspnet/core/security/https")
                .WriteLine();
            }
            else
            {
                ColoredConsole
                .WriteLine("Auto cert generation is currently not working on the .NET Core build.")
                .WriteLine("On Unix you can run:")
                .WriteLine()
                .Write(DarkGreen("sh> "))
                .WriteLine("openssl req -new -x509 -newkey rsa:2048 -keyout localhost.key -out localhost.cer -days 365 -subj /CN=localhost")
                .Write(DarkGreen("sh> "))
                .WriteLine("openssl pkcs12 -export -out certificate.pfx -inkey localhost.key -in localhost.cer")
                .WriteLine()
                .WriteLine("For more checkout https://docs.microsoft.com/en-us/aspnet/core/security/https")
                .WriteLine();
            }

            throw new CliException("Auto cert generation is currently not working on the .NET Core build.");
        }

        internal static async Task<(X509Certificate2 cert, string path, string password)> CreateCertificateOpenSSL()
        {
            const string DEFAULT_PASSWORD = "localcert";

            var certFileNames = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName().Replace(".", String.Empty));
            var output = new StringBuilder();

            ColoredConsole.WriteLine("Generating a self signed certificate using openssl");
            var opensslKey = new Executable("openssl", $"req -new -x509 -newkey rsa:2048 -nodes -keyout {certFileNames}localhost.key -out {certFileNames}localhost.cer -days 365 -subj /CN=localhost");
            var exitCode = await opensslKey.RunAsync(o => output.AppendLine(o), e => output.AppendLine(e));
            if (exitCode != 0)
            {
                ColoredConsole.Error.WriteLine(output.ToString());
                throw new CliException($"Could not create a key pair required for an openssl certificate.");
            }

            Executable openssl_cert = new Executable("openssl", $"pkcs12 -export -out {certFileNames}certificate.pfx -inkey {certFileNames}localhost.key -in {certFileNames}localhost.cer -passout pass:{DEFAULT_PASSWORD}");
            exitCode = await openssl_cert.RunAsync(o => output.AppendLine(o), e => output.AppendLine(e));
            if (exitCode != 0)
            {
                ColoredConsole.Error.WriteLine(output.ToString());
                throw new CliException($"Could not create a Certificate using openssl.");
            }

            string fullName = $"{certFileNames}certificate.pfx";
            return (new X509Certificate2($"{fullName}", DEFAULT_PASSWORD), fullName, DEFAULT_PASSWORD);
        }
    }
}
