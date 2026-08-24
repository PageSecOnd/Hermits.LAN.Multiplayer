param(
    [Parameter(Mandatory = $true)]
    [string]$StableDir,

    [Parameter(Mandatory = $true)]
    [string]$BetaDir
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\SlayTheSpire2.LAN.Multiplayer.Reforged\SlayTheSpire2.LAN.Multiplayer.Reforged.csproj"
$artifactRoot = Join-Path $PSScriptRoot "..\.artifacts\compatibility"

foreach ($channel in @(
    @{ Name = "stable"; Directory = $StableDir },
    @{ Name = "beta"; Directory = $BetaDir }
)) {
    foreach ($assembly in @("sts2.dll", "GodotSharp.dll", "0Harmony.dll")) {
        $path = Join-Path $channel.Directory $assembly
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing $assembly in $($channel.Directory)"
        }
    }

    $obj = Join-Path $artifactRoot "$($channel.Name)\obj\"
    $bin = Join-Path $artifactRoot "$($channel.Name)\bin\"
    Write-Host "Building against $($channel.Name) assemblies..."
    dotnet build $project `
        --configuration Release `
        --nologo `
        -p:Sts2Dir="$($channel.Directory)" `
        -p:BaseIntermediateOutputPath="$obj" `
        -p:BaseOutputPath="$bin"

    if ($LASTEXITCODE -ne 0) {
        throw "$($channel.Name) compatibility build failed."
    }
}

Write-Host "Stable and beta compatibility builds passed."
