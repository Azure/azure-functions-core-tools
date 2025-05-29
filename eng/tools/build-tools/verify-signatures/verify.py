import shutil
from pathlib import Path
import fnmatch
from cryptography import x509
from cryptography.hazmat.backends import default_backend
from cryptography.hazmat.primitives.serialization import pkcs7
import pefile
import argparse

# Static configuration
FILTER_EXTENSIONS = {
    ".json", "json.sha256", ".spec", ".cfg", ".pdb", ".config",
    ".nupkg", ".py", ".md"
}

RUNTIMES_TO_SIGN = [
    "min.win-arm64", "min.win-x86", "min.win-x64", "osx-arm64", "osx-x64"
]

AUTHENTICODE_BINARIES = [
    "DurableTask.AzureStorage.Internal.dll", "DurableTask.Core.Internal.dll",
    "func.dll", "func.exe", "gozip.exe", "func.pdb",
    "Microsoft.Azure.AppService.*", "Microsoft.Azure.WebJobs.*",
    "Microsoft.Azure.WebSites.DataProtection.dll",
    "Azure.Core.dll", "Azure.Identity.dll", "Azure.Storage.Blobs.dll",
    "Azure.Storage.Common.dll", "Microsoft.Extensions.Azure.dll",
    "Microsoft.Identity.Client.dll", "Microsoft.Identity.Client.Extensions.Msal.dll",
    "workers/python"
]

THIRD_PARTY_BINARIES = [
    "AccentedCommandLineParser.dll", "Autofac.dll", "Azure.Security.KeyVault.*",
    "BouncyCastle.Crypto.dll", "Colors.Net.dll", "DotNetZip.dll",
    "Dynamitey.dll", "Google.Protobuf.dll", "Grpc.AspNetCore.Server.ClientFactory.dll",
    "Grpc.AspNetCore.Server.dll", "Grpc.Core.dll", "Grpc.Core.Api.dll",
    "Grpc.Net.Client.dll", "Grpc.Net.ClientFactory.dll", "Grpc.Net.Common.dll",
    "grpc_csharp_ext.x64.dll", "grpc_csharp_ext.x86.dll", "HTTPlease.Core.dll",
    "HTTPlease.Diagnostics.dll", "HTTPlease.Formatters.dll", "HTTPlease.Formatters.Json.dll",
    "ImpromptuInterface.dll", "KubeClient.dll", "KubeClient.Extensions.KubeConfig.dll",
    "NCrontab.Signed.dll", "Newtonsoft.Json.Bson.dll", "Newtonsoft.Json.dll",
    "protobuf-net.dll", "Remotion.Linq.dll", "System.IO.Abstractions.dll",
    "dotnet-aspnet-codegenerator-design.dll", "DotNetTI.BreakingChangeAnalysis.dll",
    "Microsoft.Azure.KeyVault.*", "Microsoft.AI.*.dll", "Microsoft.Build.Framework.dll",
    "Microsoft.Build.dll", "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll",
    "Microsoft.CodeAnalysis.CSharp.Scripting.dll", "Microsoft.CodeAnalysis.CSharp.Workspaces.dll",
    "Microsoft.CodeAnalysis.Razor.dll", "Microsoft.CodeAnalysis.Scripting.dll",
    "Microsoft.CodeAnalysis.VisualBasic.dll", "Microsoft.CodeAnalysis.VisualBasic.Workspaces.dll",
    "Microsoft.CodeAnalysis.Workspaces.dll", "Microsoft.DotNet.PlatformAbstractions.dll",
    "Microsoft.Extensions.DependencyModel.dll", "Microsoft.Extensions.DiagnosticAdapter.dll",
    "Microsoft.Extensions.Logging.ApplicationInsights.dll",
    "Microsoft.Extensions.PlatformAbstractions.dll",
    "Microsoft.Azure.Services.AppAuthentication.dll", "Microsoft.IdentityModel.*",
    "Microsoft.ApplicationInsights.*", "Microsoft.Rest.ClientRuntime.*",
    "Microsoft.VisualStudio.Web.CodeGenera*", "Microsoft.WindowsAzure.Storage.dll",
    "Microsoft.AspNetCore.*", "NuGet.*.dll", "protobuf-net.Core.dll",
    "System.Composition.*", "System.Configuration.ConfigurationManager.dll",
    "System.Data.SqlClient.dll", "System.Diagnostics.PerformanceCounter.dll",
    "System.IdentityModel.Tokens.Jwt.dll", "System.Interactive.Async.dll",
    "System.Memory.Data.dll", "System.Net.Http.Formatting.dll",
    "System.Private.ServiceModel.dll", "System.Reactive.*.dll",
    "System.Security.Cryptography.ProtectedData.dll", "YamlDotNet.dll",
    "Marklio.Metadata.dll", "Microsoft.Azure.Cosmos.Table.dll",
    "Microsoft.Azure.DocumentDB.Core.dll", "Microsoft.Azure.Storage.Blob.dll",
    "Microsoft.Azure.Storage.Common.dll", "Microsoft.Azure.Storage.File.dll",
    "Microsoft.Azure.Storage.Queue.dll", "Microsoft.OData.Core.dll",
    "Microsoft.OData.Edm.dll", "Microsoft.Spatial.dll", "Mono.Posix.NETStandard.dll",
    "OpenTelemetry.Api.dll", "OpenTelemetry.Api.ProviderBuilderExtensions.dll",
    "OpenTelemetry.dll", "OpenTelemetry.Exporter.Console.dll",
    "OpenTelemetry.Exporter.OpenTelemetryProtocol.dll", "OpenTelemetry.Extensions.Hosting.dll",
    "OpenTelemetry.Instrumentation.AspNetCore.dll", "OpenTelemetry.Instrumentation.Http.dll",
    "OpenTelemetry.PersistentStorage.Abstractions.dll",
    "OpenTelemetry.PersistentStorage.FileSystem.dll", "tools/python/packapp/distlib"
]

def recursive_copy(src: Path, dst: Path):
    if dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(src, dst)

def match_binaries(base: Path, patterns: list[str]) -> list[Path]:
    matches = []
    for pattern in patterns:
        matches.extend(base.rglob(pattern) if '*' in pattern or '?' in pattern else [base / pattern])
    return [m for m in matches if m.exists()]

def remove_unsigned(files: list[Path]):
    for f in files:
        if f.suffix.lower() not in FILTER_EXTENSIONS:
            f.unlink(missing_ok=True)

def is_signed(file: Path) -> bool:
    try:
        pe = pefile.PE(str(file), fast_load=True)
        if not hasattr(pe, 'DIRECTORY_ENTRY_SECURITY'):
            return False
        pe.close()
        return True
    except Exception:
        return False

def get_unsigned_binaries(directory: Path) -> list[Path]:
    return [f for f in directory.rglob("*.dll") if not is_signed(f)] + \
           [f for f in directory.rglob("*.exe") if not is_signed(f)]

def main():
    parser = argparse.ArgumentParser(description="Verify signatures in artifact runtimes.")
    parser.add_argument("--output-dir", type=str, default="../../../../artifacts", help="Path to the output directory containing runtimes")
    args = parser.parse_args()

    output_dir = Path(args.output_dir).resolve()
    presign_test_dir = output_dir / "PreSignTest"
    presign_test_dir.mkdir(parents=True, exist_ok=True)

    for runtime in RUNTIMES_TO_SIGN:
        if runtime.startswith("osx"):
            continue

        src_dir = output_dir / runtime
        tgt_dir = presign_test_dir / runtime
        recursive_copy(src_dir, tgt_dir)

        in_proc_dir = tgt_dir / "in-proc8"
        binaries = match_binaries(tgt_dir, AUTHENTICODE_BINARIES)
        third_party = match_binaries(tgt_dir, THIRD_PARTY_BINARIES)

        if in_proc_dir.exists():
            binaries += match_binaries(in_proc_dir, AUTHENTICODE_BINARIES)
            third_party += match_binaries(in_proc_dir, THIRD_PARTY_BINARIES)

        remove_unsigned(binaries + third_party)

        unsigned = get_unsigned_binaries(tgt_dir)
        if unsigned:
            print("Unsigned files found:")
            for f in unsigned:
                print(f)
            raise SystemExit("signature verification failed.")

if __name__ == "__main__":
    main()
