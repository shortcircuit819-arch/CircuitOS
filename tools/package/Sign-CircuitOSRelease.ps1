param(
    [Parameter(Mandatory = $true)]
    [string]$CertificateThumbprint,

    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$releaseRoot = Join-Path $projectRoot "dist\CircuitOS-Release"
$executable = Join-Path $releaseRoot "CircuitOS.exe"
$zipPath = Join-Path $projectRoot "dist\CircuitOS-Windows-x64.zip"

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Build the release package before signing: $executable"
}

$certificate = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object {
    $_.Thumbprint -eq $CertificateThumbprint -and $_.HasPrivateKey
} | Select-Object -First 1
if (-not $certificate) {
    throw "A matching code-signing certificate with a private key was not found in Cert:\CurrentUser\My."
}

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
$signTool = Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $signTool) {
    throw "SignTool was not found. Install the Windows SDK signing tools."
}

& $signTool.FullName sign /sha1 $certificate.Thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $executable
if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE." }

$signature = Get-AuthenticodeSignature -LiteralPath $executable
if ($signature.Status -ne "Valid") {
    throw "The signed executable did not pass Authenticode verification: $($signature.StatusMessage)"
}

$manifestPath = Join-Path $releaseRoot "version.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "The release version manifest is missing: $manifestPath"
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$updateRoot = Join-Path $projectRoot "dist\CircuitOS-Update-$($manifest.version)"
$updateZipPath = Join-Path $projectRoot "dist\CircuitOS-Update-$($manifest.version).zip"
if (-not (Test-Path -LiteralPath $updateRoot -PathType Container)) {
    throw "The matching update folder is missing: $updateRoot"
}
Copy-Item -LiteralPath $executable -Destination (Join-Path $updateRoot "CircuitOS.exe") -Force

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
if (Test-Path -LiteralPath $updateZipPath) { Remove-Item -LiteralPath $updateZipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $releaseRoot,
    $zipPath,
    [IO.Compression.CompressionLevel]::Optimal,
    $true
)
[IO.Compression.ZipFile]::CreateFromDirectory(
    $updateRoot,
    $updateZipPath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false
)

[pscustomobject]@{
    Executable = $executable
    Publisher = $signature.SignerCertificate.Subject
    SignatureStatus = $signature.Status
    ZipFile = $zipPath
    ZipSHA256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    UpdateZipFile = $updateZipPath
    UpdateZipSHA256 = (Get-FileHash -LiteralPath $updateZipPath -Algorithm SHA256).Hash
}
