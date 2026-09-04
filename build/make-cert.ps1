<#
.SYNOPSIS
  Creates the self-signed code-signing certificate used to sign the sideload MSIX (T-44).
  Writes build\MarkPad-dev.pfx (private, git-ignored) and build\MarkPad-dev.cer (install on target PCs).
.NOTES
  The Subject must match Package.appxmanifest <Identity Publisher="CN=MarkPad Dev">.
  To trust the package on another PC (admin PowerShell):
    Import-Certificate -FilePath MarkPad-dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
#>
param([string]$Password = 'markpad-dev')
$ErrorActionPreference = 'Stop'
$dir = $PSScriptRoot
$pfx = Join-Path $dir 'MarkPad-dev.pfx'
$cer = Join-Path $dir 'MarkPad-dev.cer'
if (Test-Path $pfx) { "exists: $pfx"; return }

$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=MarkPad Dev' -KeyUsage DigitalSignature `
    -FriendlyName 'MarkPad sideload signing' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(5)
$secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
"created $pfx and $cer (thumbprint $($cert.Thumbprint))"
