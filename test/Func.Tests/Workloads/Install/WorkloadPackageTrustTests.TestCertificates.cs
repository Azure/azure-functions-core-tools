// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed partial class WorkloadPackageTrustTests
{
    private sealed class Sha1CertificateSignatureGenerator(X509Certificate2 issuer) : X509SignatureGenerator
    {
        protected override PublicKey BuildPublicKey() => issuer.PublicKey;

        public override byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm)
        {
            AsnWriter writer = new(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteObjectIdentifier("1.2.840.113549.1.1.5");
            writer.WriteNull();
            writer.PopSequence();
            return writer.Encode();
        }

        public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm)
        {
            using RSA key = issuer.GetRSAPrivateKey()!;
            return key.SignData(data, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        }
    }
}
