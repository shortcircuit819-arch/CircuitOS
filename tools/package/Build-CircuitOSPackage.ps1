param(
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$distRoot = Join-Path $projectRoot "dist"
$packageRoot = Join-Path $distRoot "CircuitOS-Release"
$zipPath = Join-Path $distRoot "CircuitOS-Windows-x64.zip"
$templateRoot = Join-Path $PSScriptRoot "package-files"
$sourceExecutable = Join-Path $projectRoot "tools\admin\runtime\CircuitOS.exe"
$releaseVersion = (Get-Item -LiteralPath $sourceExecutable).VersionInfo.ProductVersion
if (-not $releaseVersion) { throw "CircuitOS.exe does not contain a product version." }
$updateRoot = Join-Path $distRoot "CircuitOS-Update-$releaseVersion"
$updateZipPath = Join-Path $distRoot "CircuitOS-Update-$releaseVersion.zip"

function Copy-RequiredFile {
    param([string]$Source, [string]$Destination)

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required package file is missing: $Source"
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Assert-ReleaseCondition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Release validation failed: $Message" }
}

function Get-CSharpBraceDepth {
    param([string]$Source, [int]$EndExclusive = -1)
    $limit = if ($EndExclusive -ge 0) { [Math]::Min($EndExclusive, $Source.Length) } else { $Source.Length }
    $depth = 0
    $inString = $false
    $inChar = $false
    $inLineComment = $false
    $inBlockComment = $false
    $escaped = $false
    for ($index = 0; $index -lt $limit; $index++) {
        $current = $Source[$index]
        $next = if ($index + 1 -lt $limit) { $Source[$index + 1] } else { [char]0 }
        if ($inLineComment) { if ($current -eq "`n") { $inLineComment = $false }; continue }
        if ($inBlockComment) { if ($current -eq '*' -and $next -eq '/') { $inBlockComment = $false; $index++ }; continue }
        if ($inString -or $inChar) {
            if ($escaped) { $escaped = $false; continue }
            if ($current -eq '\') { $escaped = $true; continue }
            if ($inString -and $current -eq '"') { $inString = $false }
            if ($inChar -and $current -eq "'") { $inChar = $false }
            continue
        }
        if ($current -eq '/' -and $next -eq '/') { $inLineComment = $true; $index++; continue }
        if ($current -eq '/' -and $next -eq '*') { $inBlockComment = $true; $index++; continue }
        if ($current -eq '"') { $inString = $true; continue }
        if ($current -eq "'") { $inChar = $true; continue }
        if ($current -eq '{') { $depth++ }
        elseif ($current -eq '}') { $depth-- }
        if ($depth -lt 0) { return $depth }
    }
    return $depth
}

function Write-ChecksumManifest {
    param([string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $rootUri = [Uri]$rootPath
    $lines = foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object Name -ne 'checksums.sha256' | Sort-Object FullName) {
        $relative = [Uri]::UnescapeDataString($rootUri.MakeRelativeUri([Uri]$file.FullName).ToString())
        $hash = try { (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash } catch { "LOCKED" }
        "{0}  {1}" -f $hash, $relative
    }
    [IO.File]::WriteAllLines((Join-Path $Root 'checksums.sha256'), $lines, [Text.UTF8Encoding]::new($false))
}

$resolvedProject = (Resolve-Path -LiteralPath $projectRoot).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedPackage = [IO.Path]::GetFullPath($packageRoot)
if (-not $resolvedPackage.StartsWith($resolvedProject + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to package outside the project: $resolvedPackage"
}

$projectFile = [IO.File]::ReadAllText((Join-Path $projectRoot 'tools\runtime\CircuitOS.Runtime.csproj'))
$programSource = [IO.File]::ReadAllText((Join-Path $projectRoot 'tools\runtime\Program.cs'))
Assert-ReleaseCondition ($projectFile.Contains("<Version>$releaseVersion</Version>")) "runtime project version does not match CircuitOS.exe $releaseVersion."
Assert-ReleaseCondition ($programSource.Contains("version = `"$releaseVersion`"")) "health-response version does not match CircuitOS.exe $releaseVersion."

foreach ($generatedFolder in @($packageRoot, $updateRoot)) {
    if (Test-Path -LiteralPath $generatedFolder) {
        Remove-Item -LiteralPath $generatedFolder -Recurse -Force
    }
}

$folders = @(
    $packageRoot,
    (Join-Path $packageRoot "App"),
    (Join-Path $packageRoot "Data"),
    (Join-Path $packageRoot "Data\config-backups"),
    (Join-Path $packageRoot "Data\overlay"),
    (Join-Path $packageRoot "Overlay"),
    (Join-Path $packageRoot "Documentation")
)
foreach ($folder in $folders) {
    $null = New-Item -ItemType Directory -Path $folder -Force
}

Copy-RequiredFile (Join-Path $templateRoot "START HERE.txt") (Join-Path $packageRoot "START HERE.txt")
Copy-RequiredFile (Join-Path $templateRoot "OBS SETUP.txt") (Join-Path $packageRoot "Documentation\OBS SETUP.txt")
Copy-RequiredFile (Join-Path $templateRoot "RELEASE SECURITY.txt") (Join-Path $packageRoot "Documentation\RELEASE SECURITY.txt")
Copy-RequiredFile (Join-Path $templateRoot "inventory.json") (Join-Path $packageRoot "Data\inventory.json")

$appFiles = @("index.html", "app.js", "styles.css", "circuitos-icon.png")
foreach ($file in $appFiles) {
    Copy-RequiredFile (Join-Path $projectRoot "tools\admin\$file") (Join-Path $packageRoot ("App\" + [IO.Path]::GetFileName($file)))
}
Copy-RequiredFile $sourceExecutable (Join-Path $packageRoot "CircuitOS.exe")

$dataFiles = @(
    "components.json",
    "event-collection-template.json",
    "featured-boost.json",
    "overlay-config.template.json",
    "system-profile.template.json"
)
foreach ($file in $dataFiles) {
    Copy-RequiredFile (Join-Path $projectRoot "data\$file") (Join-Path $packageRoot "Data\$file")
}
Copy-RequiredFile (Join-Path $projectRoot "data\overlay-config.template.json") (Join-Path $packageRoot "Data\overlay-config.json")

$overlayFiles = @("index.html", "styles.css", "overlay.js")
foreach ($file in $overlayFiles) {
    # Overlay statics go in Overlay\ — this folder ships in both full and update packages
    # so updating CircuitOS always refreshes the overlay JS/CSS/HTML.
    # Data\overlay\ directory is kept for the native overlay to write overlay-state.json into.
    Copy-RequiredFile (Join-Path $projectRoot "overlays\lower-quarter\$file") (Join-Path $packageRoot "Overlay\$file")
}

$guideFiles = @(
    "catalog-commands.md",
    "collection-importer.md",
    "collection-command.md",
    "event-collections.md",
    "featured-stream-boosts.md",
    "leaderboard.md",
    "maintainer-quick-fixes.md",
    "rare-pull-messages.md",
    "salvage.md",
    "versioning.md"
)
foreach ($file in $guideFiles) {
    Copy-RequiredFile (Join-Path $projectRoot "docs\$file") (Join-Path $packageRoot "Documentation\$file")
}

$manifest = [ordered]@{
    schemaVersion = 1
    product = "CircuitOS"
    version = $releaseVersion
    releaseChannel = "stable"
    dataSchemaVersion = 1
    publishedUtc = [DateTime]::UtcNow.ToString("o")
}
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $packageRoot "version.json"), $manifestJson, [Text.UTF8Encoding]::new($false))

$updateFolders = @(
    $updateRoot,
    (Join-Path $updateRoot "App"),
    (Join-Path $updateRoot "Overlay"),
    (Join-Path $updateRoot "Documentation")
)
foreach ($folder in $updateFolders) {
    $null = New-Item -ItemType Directory -Path $folder -Force
}
Copy-RequiredFile (Join-Path $packageRoot "CircuitOS.exe") (Join-Path $updateRoot "CircuitOS.exe")
Copy-RequiredFile (Join-Path $packageRoot "version.json") (Join-Path $updateRoot "version.json")
Copy-RequiredFile (Join-Path $templateRoot "UPDATE README.txt") (Join-Path $updateRoot "UPDATE README.txt")
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $packageRoot "App") -File) {
    Copy-RequiredFile $file.FullName (Join-Path $updateRoot "App\$($file.Name)")
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $packageRoot "Documentation") -File) {
    Copy-RequiredFile $file.FullName (Join-Path $updateRoot "Documentation\$($file.Name)")
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $packageRoot "Overlay") -File) {
    Copy-RequiredFile $file.FullName (Join-Path $updateRoot "Overlay\$($file.Name)")
}

$packagedHtml = [IO.File]::ReadAllText((Join-Path $packageRoot 'App\index.html'))
Assert-ReleaseCondition ($packagedHtml.Contains('id="settingsNav"')) 'Settings navigation group is missing.'
Assert-ReleaseCondition (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'Data\system-profile.json'))) 'fresh package contains a live system profile.'
Assert-ReleaseCondition (-not (Test-Path -LiteralPath (Join-Path $updateRoot 'Data'))) 'update package contains a Data folder.'

Write-ChecksumManifest $packageRoot
Write-ChecksumManifest $updateRoot

if (-not $SkipZip) {
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    if (Test-Path -LiteralPath $updateZipPath) {
        Remove-Item -LiteralPath $updateZipPath -Force
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $packageRoot,
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

    foreach ($archivePath in @($zipPath, $updateZipPath)) {
        $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            Assert-ReleaseCondition (@($archive.Entries | Where-Object { $_.FullName -like '*checksums.sha256' }).Count -eq 1) "$([IO.Path]::GetFileName($archivePath)) does not contain one checksum manifest."
        }
        finally { $archive.Dispose() }
    }
}

$packageSize = (Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Measure-Object Length -Sum).Sum
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $packageRoot "CircuitOS.exe")
if ($signature.Status -ne "Valid") {
    Write-Warning "CircuitOS.exe is not trusted-code signed. Test releases may trigger reputation or antivirus warnings."
}
[pscustomobject]@{
    PackageFolder = $packageRoot
    ZipFile = if ($SkipZip) { $null } else { $zipPath }
    UpdateFolder = $updateRoot
    UpdateZipFile = if ($SkipZip) { $null } else { $updateZipPath }
    FileCount = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File).Count
    PackageBytes = $packageSize
    SignatureStatus = $signature.Status
}
