﻿using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.NativeMethods;
using Colors.Net;
using static Colors.Net.StringStaticMethods;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace Azure.Functions.Cli.Helpers
{
    internal static class SecurityHelpers
    {
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
            while (true)
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

        internal static async Task<X509Certificate2> GetOrCreateCertificate(string certPath, string certPassword)
        {
            if (!string.IsNullOrEmpty(certPath) && !string.IsNullOrEmpty(certPassword))
            {
                certPassword = File.Exists(certPassword)
                    ? File.ReadAllText(certPassword).Trim()
                    : certPassword;
                return new X509Certificate2(certPath, certPassword);
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
                .WriteLine($"$cert = {DarkYellow("New-SelfSignedCertificate")} -Subject localhost -DnsName localhost -FriendlyName \"Functions Development\" -KeyUsage DigitalSignature -TextExtension @(\"2.5.29.37={{text}}1.3.6.1.5.5.7.3.1\")")
                .Write(DarkCyan("PS> "))
                .WriteLine($"{DarkYellow("Export-PfxCertificate")} -Cert $cert -FilePath certificate.pfx -Password (ConvertTo-SecureString -String {Red("<password>")} -Force -AsPlainText)")
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

        internal static async Task<X509Certificate2> CreateCertificateOpenSSL()
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

            return new X509Certificate2($"{certFileNames}certificate.pfx", DEFAULT_PASSWORD);
        }

        public static string CalculateMd5(Stream stream)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(stream);
                var base64String = Convert.ToBase64String(hash);
                stream.Position = 0;
                return base64String;
            }
        }

        public static string CalculateMd5(string file)
        {
            using (var stream = FileSystemHelpers.OpenFile(file, FileMode.Open))
            {
                return CalculateMd5(stream);
            }
        }

        private static void EncodeLength(BinaryWriter stream, int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException("length", "Length must be non-negative");
            if (length < 0x80)
            {
                // Short form
                stream.Write((byte)length);
            }
            else
            {
                // Long form
                var temp = length;
                var bytesRequired = 0;
                while (temp > 0)
                {
                    temp >>= 8;
                    bytesRequired++;
                }
                stream.Write((byte)(bytesRequired | 0x80));
                for (var i = bytesRequired - 1; i >= 0; i--)
                {
                    stream.Write((byte)(length >> (8 * i) & 0xff));
                }
            }
        }
    }
}
