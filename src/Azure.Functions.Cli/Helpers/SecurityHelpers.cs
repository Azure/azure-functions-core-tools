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
using System.Security.Cryptography;
using System.Linq;
using System.Reflection;

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

        internal static string GetPemRsaKey(X509Certificate2 cert)
        {
            var sw = new StringWriter();
            ExportPrivateKey((RSA)cert.PrivateKey, sw);
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(sw.ToString()));
        }

        internal static string GetPemCert(X509Certificate2 cert)
        {
            var certStr = "-----BEGIN CERTIFICATE-----\n"
                    + Convert.ToBase64String(cert.Export(X509ContentType.Cert), Base64FormattingOptions.InsertLineBreaks)
                    + "\n-----END CERTIFICATE-----";
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(certStr));
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

        public static X509Certificate2 CreateCACertificate(string commonName, int validForInYears = 2)
        {
            using (var privateKey = RSA.Create(4096))
            {
                var req = new CertificateRequest($"CN={commonName}", privateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
                req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
                return req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(validForInYears));
            }
        }

        public static X509Certificate2 CreateCertificateFromCA(X509Certificate2 caCert, string commonName, IEnumerable<string> subjectAlternativeNames = null)
        {
            using (var privateKey = RSA.Create(4096))
            {
                subjectAlternativeNames = subjectAlternativeNames ?? Enumerable.Empty<string>();
                var req = new CertificateRequest($"CN={commonName}", privateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                // Set SAN names if any
                var sanBuilder = new SubjectAlternativeNameBuilder();
                foreach (var san in subjectAlternativeNames)
                {
                    sanBuilder.AddDnsName(san);
                }
                req.CertificateExtensions.Add(sanBuilder.Build());

                // not a CA cert
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

                // allow the cert to be used for TLS
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
                {
                    new Oid("1.3.6.1.5.5.7.3.2"), // client
                    new Oid("1.3.6.1.5.5.7.3.1")  // server
                },
                false));

                req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

                // https://blog.rassie.dk/2018/04/creating-an-x-509-certificate-chain-in-c/
                // This is a self-signed cert. The serial number doesn't really matter.
                // Use Unix epoch for a random-enough serial number.
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var unixTime = Convert.ToInt64((DateTime.UtcNow - epoch).TotalSeconds);
                var serialNumber = BitConverter.GetBytes(unixTime);
                return req.Create(caCert, caCert.NotBefore, caCert.NotAfter, serialNumber).CopyWithPrivateKey(privateKey);
            }
        }

        // https://stackoverflow.com/a/23739932
        private static void ExportPrivateKey(RSA csp, TextWriter outputStream)
        {
            var parameters = csp.ExportParameters(true);
            using (var stream = new MemoryStream())
            {
                var writer = new BinaryWriter(stream);
                writer.Write((byte)0x30); // SEQUENCE
                using (var innerStream = new MemoryStream())
                {
                    var innerWriter = new BinaryWriter(innerStream);
                    EncodeIntegerBigEndian(innerWriter, new byte[] { 0x00 }); // Version
                    EncodeIntegerBigEndian(innerWriter, parameters.Modulus);
                    EncodeIntegerBigEndian(innerWriter, parameters.Exponent);
                    EncodeIntegerBigEndian(innerWriter, parameters.D);
                    EncodeIntegerBigEndian(innerWriter, parameters.P);
                    EncodeIntegerBigEndian(innerWriter, parameters.Q);
                    EncodeIntegerBigEndian(innerWriter, parameters.DP);
                    EncodeIntegerBigEndian(innerWriter, parameters.DQ);
                    EncodeIntegerBigEndian(innerWriter, parameters.InverseQ);
                    var length = (int)innerStream.Length;
                    EncodeLength(writer, length);
                    writer.Write(innerStream.GetBuffer(), 0, length);
                }

                var base64 = Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length).ToCharArray();
                outputStream.WriteLine("-----BEGIN RSA PRIVATE KEY-----");
                // Output as Base64 with lines chopped at 64 characters
                for (var i = 0; i < base64.Length; i += 64)
                {
                    outputStream.WriteLine(base64, i, Math.Min(64, base64.Length - i));
                }
                outputStream.WriteLine("-----END RSA PRIVATE KEY-----");
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

        private static void EncodeIntegerBigEndian(BinaryWriter stream, byte[] value, bool forceUnsigned = true)
        {
            stream.Write((byte)0x02); // INTEGER
            var prefixZeros = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != 0) break;
                prefixZeros++;
            }
            if (value.Length - prefixZeros == 0)
            {
                EncodeLength(stream, 1);
                stream.Write((byte)0);
            }
            else
            {
                if (forceUnsigned && value[prefixZeros] > 0x7f)
                {
                    // Add a prefix zero to force unsigned if the MSB is 1
                    EncodeLength(stream, value.Length - prefixZeros + 1);
                    stream.Write((byte)0);
                }
                else
                {
                    EncodeLength(stream, value.Length - prefixZeros);
                }
                for (var i = prefixZeros; i < value.Length; i++)
                {
                    stream.Write(value[i]);
                }
            }
        }
    }
}
