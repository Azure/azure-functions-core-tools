[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Path,
    [Parameter(Mandatory)]
    [string[]] $ExpectedCommands
)

$ErrorActionPreference = 'Stop'
$result = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
$benchmarks = @($result.Benchmarks)
if ($benchmarks.Count -ne $ExpectedCommands.Count) {
    throw "Expected $($ExpectedCommands.Count) benchmark results, found $($benchmarks.Count)."
}

foreach ($command in $ExpectedCommands) {
    $commandResults = @($benchmarks | Where-Object { $_.Parameters -eq "Command=$command" })
    if ($commandResults.Count -ne 1) {
        throw "Missing or duplicate result for command '$command'."
    }
    $statistics = $commandResults[0].Statistics
    if ($null -eq $statistics -or $statistics.N -lt 1 -or
        $null -eq $statistics.Mean -or $statistics.Mean -le 0 -or
        [double]::IsNaN($statistics.Mean) -or [double]::IsInfinity($statistics.Mean)) {
        throw "Command '$command' did not produce valid timing statistics."
    }
}
