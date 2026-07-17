<#
.SYNOPSIS
    One-command Velopack release: publish -> pack (Setup.exe + update feed) -> optionally sign -> optionally upload.

.DESCRIPTION
    Produces the Velopack installer + auto-update feed for CircuitOS (docs/updater-velopack-plan.md).
    The version is read from the runtime .csproj so it can never drift from the app.

    SIGNING (docs/release-signing.md). Pick ONE:
      -CertificateThumbprint  A code-signing cert in Cert:\CurrentUser\My (traditional cert / token).
      -SignTemplate           A full custom sign command with a {{file}} placeholder — this is the
                              Azure Trusted Signing path, e.g.
                                -SignTemplate 'azuresigntool sign -kvu <url> -kvi <id> -kvs <secret> -kvc <certname> -tr http://timestamp.acs.microsoft.com -td sha256 {{file}}'
    Omit both and the output is UNSIGNED (fine for local testing; SmartScreen will warn end users).

.EXAMPLE
    # Unsigned local build
    .\Build-CircuitOSVelopack.ps1

.EXAMPLE
    # Signed with a local certificate
    .\Build-CircuitOSVelopack.ps1 -CertificateThumbprint ABC123...

.EXAMPLE
    # Signed + published to GitHub Releases (creates the public update feed)
    .\Build-CircuitOSVelopack.ps1 -CertificateThumbprint ABC123... -Upload
#>
param(
    [string]$CertificateThumbprint,
    [string]$SignTemplate,
    [switch]$Upload,
    [string]$RepoUrl = "https://github.com/shortcircuit819-arch/CircuitOS",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$SkipPublish,
    # Re-pack a version that's already in the output folder (dev only). vpk normally refuses, because
    # the folder is the release history it uses to build delta packages — don't use this on a version
    # that has actually shipped; bump the version instead.
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$csproj      = Join-Path $projectRoot "tools\runtime\CircuitOS.Runtime.csproj"
$publishDir  = Join-Path $projectRoot "tools\runtime\publish"
$outputDir   = Join-Path $projectRoot "dist\velopack"

# ── Version comes from the csproj — the single source of truth for a release ──────────────────
$version = ([xml](Get-Content -LiteralPath $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Could not read <Version> from $csproj" }
Write-Host "CircuitOS Velopack release $version" -ForegroundColor Cyan

# ── vpk CLI ──────────────────────────────────────────────────────────────────────────────────
$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    $candidate = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"
    if (Test-Path -LiteralPath $candidate) { $vpk = $candidate }
    else { throw "The Velopack CLI is missing. Install it with: dotnet tool install -g vpk" }
} else { $vpk = $vpk.Source }

# ── Publish the self-contained single-file app ────────────────────────────────────────────────
if (-not $SkipPublish) {
    Write-Host "Publishing self-contained win-x64..." -ForegroundColor Cyan
    dotnet publish $csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false `
        -p:IncludeSourceRevisionInInformationalVersion=false `
        -o $publishDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}

$exe = Join-Path $publishDir "CircuitOS.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Published executable missing: $exe" }
$productVersion = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if ($productVersion -ne $version) {
    throw "Version mismatch: csproj says $version but CircuitOS.exe reports $productVersion."
}

# ── Assemble the install payload ───────────────────────────────────────────────────────────────
# Velopack must package the WHOLE app, not just the exe: the admin UI (App\), the OBS overlay
# (Overlay\), and the starter catalog (StarterData\, which the app seeds into the per-user data folder
# on first run). Data is NOT shipped beside the exe — the versioned program folder is replaced on every
# update, so user data must live outside it. Reuse the proven ZIP layout to source these.
Write-Host "Assembling install payload..." -ForegroundColor Cyan
Copy-Item -LiteralPath $exe -Destination (Join-Path $projectRoot "tools\admin\runtime\CircuitOS.exe") -Force
& (Join-Path $PSScriptRoot "Build-CircuitOSPackage.ps1") -SkipZip | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Payload assembly (Build-CircuitOSPackage) failed." }
$releaseFolder = Join-Path $projectRoot "dist\CircuitOS-Release"

$stageDir = Join-Path $projectRoot "dist\velopack-stage"
if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
$null = New-Item -ItemType Directory -Path $stageDir -Force
Copy-Item -LiteralPath $exe -Destination (Join-Path $stageDir "CircuitOS.exe") -Force
Copy-Item -LiteralPath (Join-Path $releaseFolder "App")     -Destination (Join-Path $stageDir "App")     -Recurse -Force
Copy-Item -LiteralPath (Join-Path $releaseFolder "Overlay") -Destination (Join-Path $stageDir "Overlay") -Recurse -Force
# The assembled Data\ becomes StarterData\ — the seed source, never the live data folder.
Copy-Item -LiteralPath (Join-Path $releaseFolder "Data")    -Destination (Join-Path $stageDir "StarterData") -Recurse -Force
foreach ($required in @("App\index.html", "Overlay\overlay.js", "StarterData\components.json")) {
    if (-not (Test-Path -LiteralPath (Join-Path $stageDir $required))) { throw "Install payload is missing $required." }
}

# ── Signing arguments ────────────────────────────────────────────────────────────────────────
$signArgs = @()
if ($CertificateThumbprint -and $SignTemplate) {
    throw "Use either -CertificateThumbprint or -SignTemplate, not both."
}
if ($CertificateThumbprint) {
    $cert = Get-ChildItem -Path Cert:\CurrentUser\My |
        Where-Object { $_.Thumbprint -eq $CertificateThumbprint -and $_.HasPrivateKey } |
        Select-Object -First 1
    if (-not $cert) { throw "No code-signing certificate with a private key found for thumbprint $CertificateThumbprint in Cert:\CurrentUser\My." }
    Write-Host "Signing as: $($cert.Subject)" -ForegroundColor Cyan
    $signArgs = @("--signParams", "/sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256")
}
elseif ($SignTemplate) {
    if ($SignTemplate -notmatch "\{\{file\}\}") { throw "-SignTemplate must contain the {{file}} placeholder." }
    Write-Host "Signing via custom template (Azure Trusted Signing or similar)." -ForegroundColor Cyan
    $signArgs = @("--signTemplate", $SignTemplate)
}
else {
    Write-Warning "No signing option supplied — this build will be UNSIGNED and will trip SmartScreen for end users."
}

# ── Pack ─────────────────────────────────────────────────────────────────────────────────────
if ($Clean -and (Test-Path -LiteralPath $outputDir)) {
    Write-Warning "-Clean: removing existing packages in $outputDir (release history for deltas is lost)."
    Get-ChildItem -LiteralPath $outputDir -File | Remove-Item -Force
}
Write-Host "Packing..." -ForegroundColor Cyan
& $vpk pack --packId CircuitOS --packVersion $version --packDir $stageDir --mainExe CircuitOS.exe `
    --packTitle "CircuitOS" --packAuthors "CircuitOS" --outputDir $outputDir @signArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE." }

# ── Report + verify the signature we actually produced ───────────────────────────────────────
$setup = Join-Path $outputDir "CircuitOS-win-Setup.exe"
$signature = if (Test-Path -LiteralPath $setup) { Get-AuthenticodeSignature -LiteralPath $setup } else { $null }
if ($signature -and $signature.Status -eq "Valid") {
    Write-Host "Signature verified: $($signature.SignerCertificate.Subject)" -ForegroundColor Green
} elseif ($signature -and $signature.SignerCertificate) {
    Write-Warning "Signed but not trusted on this machine ($($signature.Status)) — expected for a self-signed/test certificate."
}

# ── Optional: publish the release + update feed to GitHub ────────────────────────────────────
if ($Upload) {
    Write-Host "Uploading release v$version to $RepoUrl ..." -ForegroundColor Cyan
    & $vpk upload github --repoUrl $RepoUrl --tag "v$version" --outputDir $outputDir --merge --releaseName "CircuitOS $version"
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed with exit code $LASTEXITCODE." }
    Write-Host "Uploaded. NOTE: the repo/releases must be PUBLIC for the app's updater to read the feed." -ForegroundColor Yellow
}

[pscustomobject]@{
    Version         = $version
    OutputDir       = $outputDir
    Setup           = $setup
    SignatureStatus = if ($signature) { $signature.Status } else { "NotSigned" }
    Signer          = if ($signature) { $signature.SignerCertificate.Subject } else { $null }
    Uploaded        = [bool]$Upload
}
