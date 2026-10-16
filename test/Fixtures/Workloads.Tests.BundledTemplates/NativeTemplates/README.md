# Controlled native packaging fixture

This first-party fixture contains inert text, not an executable project or a
product template catalog. Its project and item configurations exercise the raw
native inventory generated from an actual NuGet archive. The package carries
the repository MIT license and repository metadata.

The pack target renames the real archive to `controlled-native.nupkg` so the
shared workload target must honor the declared filename while deriving identity
and version from the inner nuspec, not the filename.
