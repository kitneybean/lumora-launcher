# Builds release archives of the launcher on Windows: artifacts/Lumora-<version>-<rid>.zip + artifacts/checksums.txt
# Usage: ./build/publish.ps1 -Version 0.2.0 -Repository owner/lumora-launcher [-Rids win-x64,linux-x64]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Repository,
    [string[]]$Rids = @("win-x64")
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "artifacts"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

foreach ($rid in $Rids) {
    $dir = Join-Path $out "publish/$rid"
    dotnet publish "$root/src/Lumora.Launcher.App/Lumora.Launcher.App.csproj" -c Release -r $rid --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
        -p:Version=$Version -p:LumoraUpdateRepo=$Repository -o $dir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }
    Remove-Item "$dir/*.pdb" -ErrorAction SilentlyContinue  # native debug symbols are not needed
    Compress-Archive -Path "$dir/*" -DestinationPath (Join-Path $out "Lumora-$Version-$rid.zip")
}

Get-ChildItem $out -Filter "Lumora-*.zip" | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content -Encoding ascii (Join-Path $out "checksums.txt")
Remove-Item (Join-Path $out "publish") -Recurse -Force
Get-ChildItem $out
