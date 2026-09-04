<#
.SYNOPSIS
  Builds the sideload MSIX (T-44): editor bundle → Release build with -p:Packaged=true → sign with build\MarkPad-dev.pfx.
  Output: out\MarkPad_<version>_x64.msix (+ the .cer to install on target PCs).
.EXAMPLE
  build\pack.ps1            # requires build\MarkPad-dev.pfx (run build\make-cert.ps1 once)
  build\pack.ps1 -NoSign    # unsigned package (for inspection only; cannot be installed)
#>
param([switch]$NoSign, [string]$Password = 'markpad-dev')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out'
New-Item -ItemType Directory -Force $out | Out-Null

if (-not (Test-Path (Join-Path $root 'src\MarkPad.App\Assets\icons\Square150x150Logo.png'))) { & (Join-Path $PSScriptRoot 'make-icons.ps1') | Out-Null }

Push-Location $root
try {
    dotnet build src\MarkPad.App\MarkPad.App.csproj -c Release -p:Packaged=true -p:AppxPackageDir="$out\"
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
} finally { Pop-Location }

$msix = Get-ChildItem $out -Recurse -Filter '*.msix' | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $msix) { throw 'no .msix produced' }
"package: $($msix.FullName) ($([math]::Round($msix.Length/1MB,1)) MB)"

if (-not $NoSign) {
    $pfx = Join-Path $PSScriptRoot 'MarkPad-dev.pfx'
    if (-not (Test-Path $pfx)) { throw "missing $pfx — run build\make-cert.ps1 first" }
    $signtool = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe not found in the Windows SDK BuildTools NuGet package' }
    & $signtool.FullName sign /fd SHA256 /a /f $pfx /p $Password $msix.FullName
    if ($LASTEXITCODE -ne 0) { throw "signing failed ($LASTEXITCODE)" }
    Copy-Item (Join-Path $PSScriptRoot 'MarkPad-dev.cer') $out -Force
    "signed. Install the certificate once (admin): Import-Certificate -FilePath $out\MarkPad-dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    "then double-click the .msix or: Add-AppxPackage $($msix.FullName)"
}
