param(
    [string]$DataPath = "C:\Users\nicho\OneDrive\Documents\CircuitComponents",
    [int]$Port = 8787,
    [switch]$NoBrowser
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$DataPath = [IO.Path]::GetFullPath($DataPath)
$UiPath = [IO.Path]::GetFullPath($PSScriptRoot)
$ProjectPath = [IO.Path]::GetFullPath((Join-Path $UiPath "..\.."))
$ActionSourcePath = Join-Path $ProjectPath "streamerbot-actions"
$ComponentsPath = Join-Path $DataPath "components.json"
$BoostPath = Join-Path $DataPath "featured-boost.json"
$InventoryPath = Join-Path $DataPath "inventory.json"
$RoleAwardsPath = Join-Path $DataPath "discord-role-awards.json"
$ProfilePath = Join-Path $DataPath "system-profile.json"
$OverlayConfigPath = Join-Path $DataPath "overlay-config.json"
$OverlayConfigTemplatePath = Join-Path $DataPath "overlay-config.template.json"
$BackupPath = Join-Path $DataPath "config-backups"

function Get-PropertyValue {
    param($Object, [string]$Name, $Default = $null)

    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function Read-JsonFile {
    param([string]$Path)

    if (-not [IO.File]::Exists($Path)) {
        throw "Required configuration file was not found: $Path"
    }

    $text = [IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Configuration file is empty: $Path"
    }

    return $text | ConvertFrom-Json
}

function Get-DefaultBoost {
    return [pscustomobject]@{
        enabled = $false
        displayName = "Featured Boost"
        collectionMultipliers = [pscustomobject]@{}
    }
}

function Get-DefaultSystemProfile {
    return [pscustomobject]@{
        schemaVersion = 1
        gameName = "Circuit Components"
        adminName = "CircuitOS Control Core"
        brandKicker = "CIRCUITOS"
        itemSingular = "component"
        itemPlural = "components"
        collectionSingular = "collection"
        collectionPlural = "collections"
        currencyName = "Scrap"
        redemptionName = "Circuit Component"
        commands = [pscustomobject]@{
            inventory = "components"
            missing = "missing"
            duplicates = "dupes"
            leaderboard = "leaderboard"
            balance = "scrap"
            collection = "collection"
            salvage = "salvage"
        }
        messages = [pscustomobject]@{
            redeemSuccess = ([char]0x26A1) + " Scan complete: @{viewer} found {item} [{collection}]. Progress: {owned}/{total}.{duplicateText}"
            rarePull = "{rareLabel}: @{viewer} pulled {item}! Current odds: about 1 in {odds}."
            triplePull = "TRIPLE MATCH: @{viewer} pulled {item} three times in a row! Sequence odds: about 1 in {odds}."
            collectionComplete = ([char]0x2705) + " COLLECTION COMPLETE: @{viewer} completed {collection}!"
            noInventory = "@{viewer} you don't have any {itemPlural} yet. Redeem {redemption} to start your {collectionSingular}."
            balance = "@{viewer} {currency} balance: {balance}."
            noDuplicates = "@{viewer} you don't have any duplicate {itemPlural} yet."
            collectionUsage = "@{viewer} usage: !{collectionCommand} <{collectionSingular}>"
            collectionSummary = "@{viewer} {collection}: {owned}/{total} | {status}{availability}"
            salvageUsage = "@{viewer} usage: !{salvageCommand} <{collectionSingular}> or !{salvageCommand} all"
            nothingToSalvage = "@{viewer} you have no extra copies to salvage in {selection}."
            salvageSuccess = "@{viewer} salvaged {count} extra {itemWord} for {earned} {currency}. Balance: {balance}."
        }
        colors = [pscustomobject]@{
            background = "#000d19"
            panel = "#061a2b"
            panelAlt = "#092239"
            line = "#193a55"
            accent = "#ff1a24"
            text = "#eef5fb"
            muted = "#8295a8"
        }
    }
}

function Test-SystemProfile {
    param($Profile)
    $errors = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Profile) { $errors.Add("System profile is required."); return $errors }
    if ((Get-IntegerValue (Get-PropertyValue $Profile "schemaVersion")) -ne 1) { $errors.Add("System profile schemaVersion must be 1.") }
    foreach ($name in @("gameName", "adminName", "brandKicker", "itemSingular", "itemPlural", "collectionSingular", "collectionPlural", "currencyName", "redemptionName")) {
        $value = [string](Get-PropertyValue $Profile $name "")
        if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -gt 80) { $errors.Add("Profile field '$name' must contain 1 to 80 characters.") }
    }
    $commands = Get-PropertyValue $Profile "commands"
    if ($null -ne $commands) {
        $seenCommands = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)
        foreach ($name in @("inventory", "missing", "duplicates", "leaderboard", "balance", "collection", "salvage")) {
            $value = [string](Get-PropertyValue $commands $name "")
            if ($value -notmatch '^[a-z0-9][a-z0-9_-]{0,30}$') { $errors.Add("Profile command '$name' must use 1 to 31 lowercase letters, numbers, underscores, or hyphens.") }
            elseif (-not $seenCommands.Add($value)) { $errors.Add("Profile command '$value' is duplicated.") }
        }
    }
    $messagePlaceholders = @{
        redeemSuccess = @("viewer", "item", "collection", "owned", "total", "duplicateText")
        rarePull = @("rareLabel", "viewer", "item", "odds")
        triplePull = @("viewer", "item", "odds")
        collectionComplete = @("viewer", "collection")
        noInventory = @("viewer", "itemPlural", "redemption", "collectionSingular")
        balance = @("viewer", "currency", "balance")
        noDuplicates = @("viewer", "itemPlural")
        collectionUsage = @("viewer", "collectionCommand", "collectionSingular")
        collectionSummary = @("viewer", "collection", "owned", "total", "status", "availability")
        salvageUsage = @("viewer", "salvageCommand", "collectionSingular")
        nothingToSalvage = @("viewer", "selection")
        salvageSuccess = @("viewer", "count", "itemWord", "earned", "currency", "balance")
    }
    $messages = Get-PropertyValue $Profile "messages"
    if ($null -ne $messages) {
        foreach ($name in $messagePlaceholders.Keys) {
            $template = [string](Get-PropertyValue $messages $name "")
            if ([string]::IsNullOrWhiteSpace($template) -or $template.Length -gt 450) {
                $errors.Add("Message template '$name' must contain 1 to 450 characters.")
                continue
            }
            $allowed = $messagePlaceholders[$name]
            foreach ($match in [regex]::Matches($template, '\{([a-zA-Z][a-zA-Z0-9]*)\}')) {
                if ($allowed -notcontains $match.Groups[1].Value) { $errors.Add("Message template '$name' uses unsupported placeholder '$($match.Value)'.") }
            }
            $withoutTokens = [regex]::Replace($template, '\{[a-zA-Z][a-zA-Z0-9]*\}', '')
            if ($withoutTokens.Contains('{') -or $withoutTokens.Contains('}')) { $errors.Add("Message template '$name' contains an invalid placeholder brace.") }
        }
    }
    $colors = Get-PropertyValue $Profile "colors"
    if ($null -eq $colors) { $errors.Add("System profile colors are required."); return $errors }
    foreach ($name in @("background", "panel", "panelAlt", "line", "accent", "text", "muted")) {
        $value = [string](Get-PropertyValue $colors $name "")
        if ($value -notmatch '^#[0-9a-fA-F]{6}$') { $errors.Add("Profile color '$name' must be a six-digit hex color.") }
    }
    return $errors
}

function Normalize-SystemProfile {
    param($Profile)
    $defaults = Get-DefaultSystemProfile
    if ($null -eq $Profile) { return $defaults }
    $normalized = Get-DefaultSystemProfile
    foreach ($name in @("gameName", "adminName", "brandKicker", "itemSingular", "itemPlural", "collectionSingular", "collectionPlural", "currencyName", "redemptionName")) {
        $value = Get-PropertyValue $Profile $name
        if ($null -ne $value) { $normalized.$name = [string]$value }
    }
    $incomingColors = Get-PropertyValue $Profile "colors"
    if ($null -ne $incomingColors) {
        foreach ($name in @("background", "panel", "panelAlt", "line", "accent", "text", "muted")) {
            $value = Get-PropertyValue $incomingColors $name
            if ($null -ne $value) { $normalized.colors.$name = [string]$value }
        }
    }
    $incomingCommands = Get-PropertyValue $Profile "commands"
    if ($null -ne $incomingCommands) {
        foreach ($name in @("inventory", "missing", "duplicates", "leaderboard", "balance", "collection", "salvage")) {
            $value = Get-PropertyValue $incomingCommands $name
            if ($null -ne $value) { $normalized.commands.$name = [string]$value }
        }
    }
    $incomingMessages = Get-PropertyValue $Profile "messages"
    if ($null -ne $incomingMessages) {
        foreach ($name in @("redeemSuccess", "rarePull", "triplePull", "collectionComplete", "noInventory", "balance", "noDuplicates", "collectionUsage", "collectionSummary", "salvageUsage", "nothingToSalvage", "salvageSuccess")) {
            $value = Get-PropertyValue $incomingMessages $name
            if ($null -ne $value) { $normalized.messages.$name = [string]$value }
        }
    }
    return $normalized
}

function Get-SystemProfile {
    $configured = [IO.File]::Exists($ProfilePath)
    $rawProfile = if ($configured) { Read-JsonFile $ProfilePath } else { $null }
    $profile = Normalize-SystemProfile $rawProfile
    return [pscustomobject]@{
        profile = $profile
        isConfigured = $configured
        validationErrors = @(Test-SystemProfile $profile)
        dataPath = $DataPath
    }
}

function Save-SystemProfile {
    param($Profile)
    $Profile = Normalize-SystemProfile $Profile
    $errors = @(Test-SystemProfile $Profile)
    if ($errors.Count -gt 0) { return [pscustomobject]@{ ok = $false; status = 400; errors = $errors } }
    $timestamp = [DateTime]::Now.ToString("yyyyMMdd_HHmmss_fff")
    $backup = Write-AtomicJson $ProfilePath $Profile "system-profile" $timestamp
    return [pscustomobject]@{
        ok = $true
        status = 200
        savedAtUtc = [DateTime]::UtcNow.ToString("o")
        backup = $backup
    }
}

function Test-Configuration {
    param($Components, $Boost)

    $errors = New-Object System.Collections.Generic.List[string]
    $collections = Get-PropertyValue $Components "collections"

    if ($null -eq $collections) {
        $errors.Add("components.json needs a top-level collections object.")
        return $errors
    }

    $collectionProperties = @($collections.PSObject.Properties)
    if ($collectionProperties.Count -eq 0) {
        $errors.Add("At least one collection is required.")
        return $errors
    }

    $componentIds = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)
    $collectionKeys = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)

    foreach ($collectionProperty in $collectionProperties) {
        $key = [string]$collectionProperty.Name
        $collection = $collectionProperty.Value

        if ($key -notmatch '^[a-z0-9][a-z0-9_]*$') {
            $errors.Add("Collection key '$key' must use lowercase letters, numbers, and underscores.")
        }

        if (-not $collectionKeys.Add($key)) {
            $errors.Add("Duplicate collection key: $key")
        }

        $displayName = [string](Get-PropertyValue $collection "displayName" "")
        if ([string]::IsNullOrWhiteSpace($displayName)) {
            $errors.Add("Collection '$key' needs a displayName.")
        }

        $type = [string](Get-PropertyValue $collection "type" "permanent")
        if ($type -ne "permanent" -and $type -ne "event") {
            $errors.Add("Collection '$key' type must be permanent or event.")
        }

        try {
            $weight = [double](Get-PropertyValue $collection "weight")
            if ($weight -lt 0) { throw "negative" }
        }
        catch {
            $errors.Add("Collection '$key' weight must be zero or greater.")
        }

        try {
            $salvageValue = [long](Get-PropertyValue $collection "salvageValue")
            if ($salvageValue -le 0) { throw "nonpositive" }
        }
        catch {
            $errors.Add("Collection '$key' salvageValue must be a positive integer.")
        }

        if ($type -eq "event") {
            $enabled = Get-PropertyValue $collection "enabled"
            if ($enabled -isnot [bool]) {
                $errors.Add("Event '$key' enabled must be true or false.")
            }

            $fromText = [string](Get-PropertyValue $collection "activeFromUtc" "")
            $untilText = [string](Get-PropertyValue $collection "activeUntilUtc" "")
            $from = [DateTimeOffset]::MinValue
            $until = [DateTimeOffset]::MinValue
            $validFrom = [DateTimeOffset]::TryParse($fromText, [ref]$from)
            $validUntil = [DateTimeOffset]::TryParse($untilText, [ref]$until)

            if (-not $validFrom -or -not $validUntil -or $until -le $from) {
                $errors.Add("Event '$key' needs a valid UTC start before its UTC end.")
            }
        }

        $parts = @(Get-PropertyValue $collection "parts" @())
        if ($parts.Count -eq 0) {
            $errors.Add("Collection '$key' needs at least one component.")
        }

        foreach ($part in $parts) {
            $partId = [string](Get-PropertyValue $part "id" "")
            $partName = [string](Get-PropertyValue $part "name" "")

            if ($partId -notmatch '^[a-z0-9][a-z0-9_]*$') {
                $errors.Add("Component ID '$partId' in '$key' is invalid.")
            }
            elseif (-not $componentIds.Add($partId)) {
                $errors.Add("Component ID '$partId' is duplicated in the catalog.")
            }

            if ([string]::IsNullOrWhiteSpace($partName)) {
                $errors.Add("Component '$partId' in '$key' needs a name.")
            }
        }
    }

    if ($null -eq $Boost) {
        $errors.Add("Featured boost configuration is missing.")
        return $errors
    }

    $boostEnabled = Get-PropertyValue $Boost "enabled"
    if ($boostEnabled -isnot [bool]) {
        $errors.Add("Boost enabled must be true or false.")
    }

    $boostName = [string](Get-PropertyValue $Boost "displayName" "")
    $multipliers = Get-PropertyValue $Boost "collectionMultipliers"
    $multiplierProperties = @()
    if ($null -ne $multipliers) {
        $multiplierProperties = @($multipliers.PSObject.Properties)
    }

    if ($boostEnabled -eq $true -and [string]::IsNullOrWhiteSpace($boostName)) {
        $errors.Add("An enabled boost needs a displayName.")
    }

    if ($boostEnabled -eq $true -and $multiplierProperties.Count -eq 0) {
        $errors.Add("An enabled boost needs at least one multiplier.")
    }

    foreach ($multiplierProperty in $multiplierProperties) {
        if (-not $collectionKeys.Contains($multiplierProperty.Name)) {
            $errors.Add("Boost references unknown collection '$($multiplierProperty.Name)'.")
        }

        try {
            $multiplier = [double]$multiplierProperty.Value
            if ($multiplier -le 0) { throw "nonpositive" }
        }
        catch {
            $errors.Add("Boost multiplier for '$($multiplierProperty.Name)' must be greater than zero.")
        }
    }

    return $errors
}

function Write-AtomicJson {
    param([string]$Path, $Value, [string]$BackupLabel, [string]$Timestamp)

    $json = $Value | ConvertTo-Json -Depth 100
    $null = $json | ConvertFrom-Json
    $directory = [IO.Path]::GetDirectoryName($Path)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.Directory]::CreateDirectory($BackupPath) | Out-Null
    $tempPath = Join-Path $directory ("." + [IO.Path]::GetFileName($Path) + "." + [Guid]::NewGuid().ToString("N") + ".tmp")
    $backupFile = Join-Path $BackupPath ($BackupLabel + "_" + $Timestamp + ".json")

    try {
        [IO.File]::WriteAllText($tempPath, $json, (New-Object Text.UTF8Encoding($false)))
        $null = [IO.File]::ReadAllText($tempPath) | ConvertFrom-Json

        if ([IO.File]::Exists($Path)) {
            [IO.File]::Replace($tempPath, $Path, $backupFile)
        }
        else {
            [IO.File]::Move($tempPath, $Path)
            $backupFile = $null
        }
    }
    finally {
        if ([IO.File]::Exists($tempPath)) {
            try { [IO.File]::Delete($tempPath) } catch { }
        }
    }

    return $backupFile
}

function Get-OverlayConfig {
    $configured = [IO.File]::Exists($OverlayConfigPath)
    $config = if ($configured) { Read-JsonFile $OverlayConfigPath } else { Read-JsonFile $OverlayConfigTemplatePath }
    return [pscustomobject]@{ ok = $true; isConfigured = $configured; config = $config }
}

function Save-OverlayConfig {
    param($Body)
    if ($null -eq $Body.config -or [int]$Body.config.schemaVersion -ne 1) {
        return [pscustomobject]@{ ok = $false; status = 400; errors = @("Overlay config schemaVersion must be 1.") }
    }
    $backup = Write-AtomicJson $OverlayConfigPath $Body.config "overlay-config" (Get-Date -Format "yyyyMMdd_HHmmss_fff")
    return [pscustomobject]@{ ok = $true; status = 200; backup = $backup; config = $Body.config }
}

function Get-Configuration {
    $components = Read-JsonFile $ComponentsPath
    $boost = if ([IO.File]::Exists($BoostPath)) { Read-JsonFile $BoostPath } else { Get-DefaultBoost }
    $errors = @(Test-Configuration $components $boost)

    return [pscustomobject]@{
        components = $components
        boost = $boost
        validationErrors = $errors
        dataPath = $DataPath
        loadedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
}

function Get-IntegerValue {
    param($Value, [long]$Default = 0)

    if ($null -eq $Value) { return $Default }
    try { return [long]$Value } catch { return $Default }
}

function Get-SalvageValue {
    param([string]$CollectionKey, $Collection)

    $configured = Get-PropertyValue $Collection "salvageValue"
    if ($null -ne $configured) { return [long]$configured }
    switch ($CollectionKey) {
        "basic" { return 1 }
        "power" { return 2 }
        "advanced" { return 3 }
        "broken" { return 5 }
        "quantum" { return 10 }
        default { return 0 }
    }
}

function Get-InventoryAnalytics {
    if (-not [IO.File]::Exists($InventoryPath)) {
        return [pscustomobject]@{
            summary = [pscustomobject]@{ viewerCount = 0; totalScrap = 0; averageScrap = 0; medianScrap = 0; duplicateUnits = 0; unclaimedScrap = 0 }
            collections = @()
            viewers = @()
            generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        }
    }

    $inventory = Read-JsonFile $InventoryPath
    $catalog = Read-JsonFile $ComponentsPath
    $catalogCollections = Get-PropertyValue $catalog "collections"
    $collectionMetrics = @{}

    foreach ($collectionProperty in @($catalogCollections.PSObject.Properties)) {
        $collection = $collectionProperty.Value
        $parts = @(Get-PropertyValue $collection "parts" @())
        $collectionMetrics[$collectionProperty.Name] = [pscustomobject]@{
            key = $collectionProperty.Name
            displayName = [string](Get-PropertyValue $collection "displayName" $collectionProperty.Name)
            type = [string](Get-PropertyValue $collection "type" "permanent")
            salvageValue = Get-SalvageValue $collectionProperty.Name $collection
            partCount = $parts.Count
            viewerOwners = 0L
            uniqueOwned = 0L
            duplicateUnits = 0L
            unclaimedScrap = 0L
        }
    }

    $viewerRows = New-Object System.Collections.Generic.List[object]
    $scrapBalances = New-Object System.Collections.Generic.List[long]
    [long]$totalScrap = 0
    [long]$totalDuplicateUnits = 0
    [long]$totalUnclaimedScrap = 0
    [long]$totalOwnedUnits = 0
    [long]$totalUniqueOwned = 0

    foreach ($viewerProperty in @($inventory.PSObject.Properties)) {
        $viewerId = [string]$viewerProperty.Name
        $viewer = $viewerProperty.Value
        $components = Get-PropertyValue $viewer "components"
        if ($null -eq $components) { continue }

        $displayName = [string](Get-PropertyValue $viewer "displayName" $viewerId)
        $wallet = Get-PropertyValue $viewer "wallet"
        $completed = Get-PropertyValue $viewer "completedCollections"
        [long]$scrap = Get-IntegerValue (Get-PropertyValue $wallet "scrap")
        [long]$viewerUnits = 0
        [long]$viewerUnique = 0
        [long]$viewerDuplicates = 0
        [long]$viewerUnclaimedScrap = 0
        [int]$viewerCompletions = 0
        $viewerCollections = New-Object System.Collections.Generic.List[object]

        foreach ($collectionProperty in @($catalogCollections.PSObject.Properties)) {
            $key = [string]$collectionProperty.Name
            $collection = $collectionProperty.Value
            $parts = @(Get-PropertyValue $collection "parts" @())
            [long]$salvageValue = Get-SalvageValue $key $collection
            [int]$ownedCount = 0
            [long]$collectionUnits = 0
            [long]$collectionDuplicates = 0
            $partRows = New-Object System.Collections.Generic.List[object]

            foreach ($part in $parts) {
                $partId = [string](Get-PropertyValue $part "id" "")
                $partName = [string](Get-PropertyValue $part "name" $partId)
                [long]$quantity = Get-IntegerValue (Get-PropertyValue $components $partId)
                if ($quantity -lt 0) { $quantity = 0 }
                [long]$duplicates = [Math]::Max(0L, $quantity - 1L)

                if ($quantity -gt 0) {
                    $ownedCount++
                    $viewerUnique++
                    $collectionMetrics[$key].uniqueOwned++
                }

                $collectionUnits += $quantity
                $collectionDuplicates += $duplicates
                $viewerUnits += $quantity
                $viewerDuplicates += $duplicates
                $viewerUnclaimedScrap += $duplicates * $salvageValue

                $partRows.Add([pscustomobject]@{
                    id = $partId
                    name = $partName
                    quantity = $quantity
                })
            }

            $completionDate = [string](Get-PropertyValue $completed $key "")
            if (-not [string]::IsNullOrWhiteSpace($completionDate)) { $viewerCompletions++ }
            if ($ownedCount -gt 0) { $collectionMetrics[$key].viewerOwners++ }
            $collectionMetrics[$key].duplicateUnits += $collectionDuplicates
            $collectionMetrics[$key].unclaimedScrap += $collectionDuplicates * $salvageValue

            $viewerCollections.Add([pscustomobject]@{
                key = $key
                displayName = [string](Get-PropertyValue $collection "displayName" $key)
                type = [string](Get-PropertyValue $collection "type" "permanent")
                ownedCount = $ownedCount
                totalCount = $parts.Count
                totalUnits = $collectionUnits
                duplicateUnits = $collectionDuplicates
                completionDate = $completionDate
                parts = $partRows
            })
        }

        $totalScrap += $scrap
        $totalDuplicateUnits += $viewerDuplicates
        $totalUnclaimedScrap += $viewerUnclaimedScrap
        $totalOwnedUnits += $viewerUnits
        $totalUniqueOwned += $viewerUnique
        $scrapBalances.Add($scrap)

        $viewerRows.Add([pscustomobject]@{
            id = $viewerId
            displayName = $displayName
            scrap = $scrap
            totalUnits = $viewerUnits
            uniqueComponents = $viewerUnique
            duplicateUnits = $viewerDuplicates
            unclaimedScrap = $viewerUnclaimedScrap
            completedCollections = $viewerCompletions
            collections = $viewerCollections
        })
    }

    $sortedBalances = @($scrapBalances | Sort-Object)
    [double]$averageScrap = if ($scrapBalances.Count -gt 0) { $totalScrap / [double]$scrapBalances.Count } else { 0 }
    [double]$medianScrap = 0
    if ($sortedBalances.Count -gt 0) {
        $middle = [int][Math]::Floor($sortedBalances.Count / 2)
        $medianScrap = if ($sortedBalances.Count % 2 -eq 1) { $sortedBalances[$middle] } else { ($sortedBalances[$middle - 1] + $sortedBalances[$middle]) / 2.0 }
    }

    return [pscustomobject]@{
        summary = [pscustomobject]@{
            viewerCount = $viewerRows.Count
            totalScrap = $totalScrap
            averageScrap = [Math]::Round($averageScrap, 2)
            medianScrap = [Math]::Round($medianScrap, 2)
            duplicateUnits = $totalDuplicateUnits
            unclaimedScrap = $totalUnclaimedScrap
            totalOwnedUnits = $totalOwnedUnits
            totalUniqueOwned = $totalUniqueOwned
        }
        collections = @($collectionMetrics.Values | Sort-Object displayName)
        viewers = @($viewerRows | Sort-Object displayName)
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
}

function Get-DefaultRoleAwardsState {
    $catalog = Read-JsonFile $ComponentsPath
    $catalogCollections = Get-PropertyValue $catalog "collections"
    $roleNames = [ordered]@{}
    foreach ($collectionProperty in @($catalogCollections.PSObject.Properties)) {
        $displayName = [string](Get-PropertyValue $collectionProperty.Value "displayName" $collectionProperty.Name)
        $roleNames[$collectionProperty.Name] = "$displayName Collector"
    }
    return [pscustomobject]@{
        roleNames = [pscustomobject]$roleNames
        fulfilled = [pscustomobject]@{}
    }
}

function Get-RoleAwardsState {
    if (-not [IO.File]::Exists($RoleAwardsPath)) { return Get-DefaultRoleAwardsState }
    $state = Read-JsonFile $RoleAwardsPath
    if ($null -eq (Get-PropertyValue $state "roleNames")) { $state | Add-Member NoteProperty roleNames ([pscustomobject]@{}) -Force }
    if ($null -eq (Get-PropertyValue $state "fulfilled")) { $state | Add-Member NoteProperty fulfilled ([pscustomobject]@{}) -Force }
    return $state
}

function Get-DiscordRoleAwards {
    $catalog = Read-JsonFile $ComponentsPath
    $catalogCollections = Get-PropertyValue $catalog "collections"
    $state = Get-RoleAwardsState
    $roleNames = [ordered]@{}
    foreach ($collectionProperty in @($catalogCollections.PSObject.Properties)) {
        $key = [string]$collectionProperty.Name
        $displayName = [string](Get-PropertyValue $collectionProperty.Value "displayName" $key)
        $configuredName = [string](Get-PropertyValue $state.roleNames $key "$displayName Collector")
        $roleNames[$key] = if ([string]::IsNullOrWhiteSpace($configuredName)) { "$displayName Collector" } else { $configuredName }
    }

    $awards = New-Object System.Collections.Generic.List[object]
    if ([IO.File]::Exists($InventoryPath)) {
        $inventory = Read-JsonFile $InventoryPath
        foreach ($viewerProperty in @($inventory.PSObject.Properties)) {
            $viewerId = [string]$viewerProperty.Name
            $viewer = $viewerProperty.Value
            $displayName = [string](Get-PropertyValue $viewer "displayName" $viewerId)
            $completed = Get-PropertyValue $viewer "completedCollections"
            if ($null -eq $completed) { continue }
            foreach ($completionProperty in @($completed.PSObject.Properties)) {
                $collectionKey = [string]$completionProperty.Name
                $collection = Get-PropertyValue $catalogCollections $collectionKey
                if ($null -eq $collection) { continue }
                $completedAtUtc = [string]$completionProperty.Value
                if ([string]::IsNullOrWhiteSpace($completedAtUtc)) { continue }
                $awardKey = "$viewerId::$collectionKey"
                $fulfillment = Get-PropertyValue $state.fulfilled $awardKey
                $assignedAtUtc = [string](Get-PropertyValue $fulfillment "assignedAtUtc" "")
                $collectionName = [string](Get-PropertyValue $collection "displayName" $collectionKey)
                $awards.Add([pscustomobject]@{
                    awardKey = $awardKey
                    userId = $viewerId
                    displayName = $displayName
                    collectionKey = $collectionKey
                    collectionName = $collectionName
                    roleName = [string]$roleNames[$collectionKey]
                    completedAtUtc = $completedAtUtc
                    assignedAtUtc = $assignedAtUtc
                    assigned = -not [string]::IsNullOrWhiteSpace($assignedAtUtc)
                })
            }
        }
    }

    $orderedAwards = @($awards | Sort-Object @{ Expression = "assigned"; Ascending = $true }, @{ Expression = "completedAtUtc"; Descending = $true })
    return [pscustomobject]@{
        roleNames = [pscustomobject]$roleNames
        awards = $orderedAwards
        summary = [pscustomobject]@{
            pending = @($orderedAwards | Where-Object { -not $_.assigned }).Count
            assigned = @($orderedAwards | Where-Object { $_.assigned }).Count
            total = $orderedAwards.Count
        }
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
}

function Update-DiscordRoleAwards {
    param($RequestBody)

    $operation = [string](Get-PropertyValue $RequestBody "operation" "")
    $state = Get-RoleAwardsState
    $catalog = Read-JsonFile $ComponentsPath
    $catalogCollections = Get-PropertyValue $catalog "collections"

    if ($operation -eq "saveRoleNames") {
        $requestedNames = Get-PropertyValue $RequestBody "roleNames"
        if ($null -eq $requestedNames) { return [pscustomobject]@{ ok = $false; status = 400; errors = @("roleNames is required.") } }
        $roleNames = [ordered]@{}
        foreach ($collectionProperty in @($catalogCollections.PSObject.Properties)) {
            $key = [string]$collectionProperty.Name
            $name = [string](Get-PropertyValue $requestedNames $key "")
            if ([string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 100) {
                return [pscustomobject]@{ ok = $false; status = 400; errors = @("Role name for '$key' must contain 1 to 100 characters.") }
            }
            $roleNames[$key] = $name.Trim()
        }
        $state.roleNames = [pscustomobject]$roleNames
    }
    elseif ($operation -eq "setAssigned") {
        $viewerId = [string](Get-PropertyValue $RequestBody "userId" "")
        $collectionKey = [string](Get-PropertyValue $RequestBody "collectionKey" "")
        $assigned = Get-PropertyValue $RequestBody "assigned"
        if ([string]::IsNullOrWhiteSpace($viewerId) -or [string]::IsNullOrWhiteSpace($collectionKey) -or $assigned -isnot [bool]) {
            return [pscustomobject]@{ ok = $false; status = 400; errors = @("A viewer, collection, and boolean assigned value are required.") }
        }
        if ($null -eq (Get-PropertyValue $catalogCollections $collectionKey)) {
            return [pscustomobject]@{ ok = $false; status = 400; errors = @("Unknown collection '$collectionKey'.") }
        }
        if (-not [IO.File]::Exists($InventoryPath)) {
            return [pscustomobject]@{ ok = $false; status = 400; errors = @("Inventory is unavailable.") }
        }
        $inventory = Read-JsonFile $InventoryPath
        $viewer = Get-PropertyValue $inventory $viewerId
        $completion = Get-PropertyValue (Get-PropertyValue $viewer "completedCollections") $collectionKey
        if ([string]::IsNullOrWhiteSpace([string]$completion)) {
            return [pscustomobject]@{ ok = $false; status = 400; errors = @("This viewer does not have a recorded completion for '$collectionKey'.") }
        }
        $awardKey = "$viewerId::$collectionKey"
        if ($assigned) {
            $roleName = [string](Get-PropertyValue $state.roleNames $collectionKey "")
            $state.fulfilled | Add-Member NoteProperty $awardKey ([pscustomobject]@{
                assignedAtUtc = [DateTime]::UtcNow.ToString("o")
                roleName = $roleName
            }) -Force
        }
        else {
            $state.fulfilled.PSObject.Properties.Remove($awardKey)
        }
    }
    else {
        return [pscustomobject]@{ ok = $false; status = 400; errors = @("Unknown role award operation.") }
    }

    $timestamp = [DateTime]::Now.ToString("yyyyMMdd_HHmmss_fff")
    $backup = Write-AtomicJson $RoleAwardsPath $state "discord-role-awards" $timestamp
    return [pscustomobject]@{
        ok = $true
        status = 200
        savedAtUtc = [DateTime]::UtcNow.ToString("o")
        backup = $backup
    }
}

function ConvertTo-CSharpStringContent {
    param([string]$Value)
    if ($null -eq $Value) { return "" }
    return $Value.Replace("\", "\\").Replace('"', '\"').Replace("`r", "").Replace("`n", "\n")
}

function Get-GeneratedActionSource {
    param([string]$FileName, $Profile)

    $allowed = @("StreamerbotReedeem.txt", "StreamerbotCatalogCommands.txt", "StreamerbotCollection.txt", "StreamerbotSalvage.txt")
    if ($allowed -notcontains $FileName) { throw "Unknown Streamer.bot action source." }
    $sourcePath = [IO.Path]::GetFullPath((Join-Path $ActionSourcePath $FileName))
    $sourceRoot = [IO.Path]::GetFullPath($ActionSourcePath).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $sourcePath.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.File]::Exists($sourcePath)) {
        throw "Streamer.bot action source is unavailable: $FileName"
    }

    $source = [IO.File]::ReadAllText($sourcePath)
    $escapedPath = $DataPath.Replace('"', '""')
    $pathPattern = New-Object Text.RegularExpressions.Regex('string folderPath = @"[^"]*";')
    $source = $pathPattern.Replace($source, "string folderPath = @`"$escapedPath`";", 1)
    $templateFields = @{}
    if ($FileName -eq "StreamerbotReedeem.txt") {
        $templateFields = @{ RedeemSuccessTemplate = "redeemSuccess"; RarePullTemplate = "rarePull"; TriplePullTemplate = "triplePull"; CollectionCompleteTemplate = "collectionComplete" }
    }
    elseif ($FileName -eq "StreamerbotCatalogCommands.txt") {
        $templateFields = @{ NoInventoryTemplate = "noInventory"; BalanceTemplate = "balance"; NoDuplicatesTemplate = "noDuplicates" }
    }
    elseif ($FileName -eq "StreamerbotCollection.txt") {
        $templateFields = @{ CollectionUsageTemplate = "collectionUsage"; CollectionSummaryTemplate = "collectionSummary" }
    }
    elseif ($FileName -eq "StreamerbotSalvage.txt") {
        $templateFields = @{ SalvageUsageTemplate = "salvageUsage"; NothingToSalvageTemplate = "nothingToSalvage"; SalvageSuccessTemplate = "salvageSuccess" }
    }
    $defaultMessages = (Get-DefaultSystemProfile).messages
    foreach ($entry in $templateFields.GetEnumerator()) {
        $defaultText = ConvertTo-CSharpStringContent ([string](Get-PropertyValue $defaultMessages $entry.Value ""))
        $customText = ConvertTo-CSharpStringContent ([string](Get-PropertyValue $Profile.messages $entry.Value ""))
        $source = $source.Replace(
            "private const string $($entry.Key) = `"$defaultText`";",
            "private const string $($entry.Key) = `"$customText`";"
        )
    }
    $source = $source.Replace("Circuit Components", (ConvertTo-CSharpStringContent ([string]$Profile.gameName)))
    $source = $source.Replace("Circuit Component", (ConvertTo-CSharpStringContent ([string]$Profile.redemptionName)))
    $source = $source.Replace("Scrap", (ConvertTo-CSharpStringContent ([string]$Profile.currencyName)))
    $source = $source.Replace('"itemPlural", "components"', '"itemPlural", "' + (ConvertTo-CSharpStringContent ([string]$Profile.itemPlural)) + '"')
    $source = $source.Replace('"collectionSingular", "collection"', '"collectionSingular", "' + (ConvertTo-CSharpStringContent ([string]$Profile.collectionSingular)) + '"')
    $source = $source.Replace('"collectionCommand", "collection"', '"collectionCommand", "' + (ConvertTo-CSharpStringContent ([string]$Profile.commands.collection)) + '"')
    $source = $source.Replace('"salvageCommand", "salvage"', '"salvageCommand", "' + (ConvertTo-CSharpStringContent ([string]$Profile.commands.salvage)) + '"')
    $source = $source.Replace('consumedComponents == 1 ? "component" : "components"', 'consumedComponents == 1 ? "' + (ConvertTo-CSharpStringContent ([string]$Profile.itemSingular)) + '" : "' + (ConvertTo-CSharpStringContent ([string]$Profile.itemPlural)) + '"')
    $source = $source.Replace('!salvage', '!' + [string]$Profile.commands.salvage)
    if ($FileName -eq "StreamerbotCatalogCommands.txt") {
        $commandMap = @{
            components = [string]$Profile.commands.inventory
            missing = [string]$Profile.commands.missing
            dupes = [string]$Profile.commands.duplicates
            leaderboard = [string]$Profile.commands.leaderboard
            scrap = [string]$Profile.commands.balance
        }
        foreach ($entry in $commandMap.GetEnumerator()) {
            $source = $source.Replace("commandName != `"$($entry.Key)`"", "commandName != `"$($entry.Value)`"")
            $source = $source.Replace("commandName == `"$($entry.Key)`"", "commandName == `"$($entry.Value)`"")
        }
    }
    foreach ($entry in $templateFields.GetEnumerator()) {
        $customText = ConvertTo-CSharpStringContent ([string](Get-PropertyValue $Profile.messages $entry.Value ""))
        $constantPattern = New-Object Text.RegularExpressions.Regex(
            'private const string ' + [regex]::Escape([string]$entry.Key) + ' = "(?:\\.|[^"])*";'
        )
        $replacement = "private const string $($entry.Key) = `"$customText`";"
        $source = $constantPattern.Replace($source, $replacement.Replace('$', '$$'), 1)
    }
    return $source
}

function Get-StreamerBotSetup {
    param($RequestedProfile)

    $profile = if ($null -eq $RequestedProfile) { (Get-SystemProfile).profile } else { Normalize-SystemProfile $RequestedProfile }
    $errors = @(Test-SystemProfile $profile)
    if ($errors.Count -gt 0) { return [pscustomobject]@{ ok = $false; status = 400; errors = $errors } }

    $actions = @(
        [pscustomobject]@{
            key = "redeem"
            name = [string]$profile.redemptionName
            description = "Awards a weighted item, records completion, creates inventory backups, and updates the OBS overlay state."
            triggers = @("Channel Point Reward: $($profile.redemptionName)")
            references = @("Newtonsoft.Json", "Microsoft.CSharp")
            source = Get-GeneratedActionSource "StreamerbotReedeem.txt" $profile
        },
        [pscustomobject]@{
            key = "catalog"
            name = "$($profile.gameName) Commands"
            description = "Handles progress, missing items, duplicates, leaderboard, and currency balance commands."
            triggers = @("!$($profile.commands.inventory)", "!$($profile.commands.missing)", "!$($profile.commands.duplicates)", "!$($profile.commands.leaderboard)", "!$($profile.commands.balance)")
            references = @()
            source = Get-GeneratedActionSource "StreamerbotCatalogCommands.txt" $profile
        },
        [pscustomobject]@{
            key = "collection"
            name = "$($profile.gameName) Collection Detail"
            description = "Shows a viewer's progress in one named collection."
            triggers = @("!$($profile.commands.collection)")
            references = @()
            source = Get-GeneratedActionSource "StreamerbotCollection.txt" $profile
        },
        [pscustomobject]@{
            key = "salvage"
            name = "$($profile.gameName) Salvage"
            description = "Converts duplicate items into $($profile.currencyName) with inventory locking and backups."
            triggers = @("!$($profile.commands.salvage)", "!$($profile.commands.salvage) <$($profile.collectionSingular)>")
            references = @()
            source = Get-GeneratedActionSource "StreamerbotSalvage.txt" $profile
        }
    )
    return [pscustomobject]@{
        ok = $true
        status = 200
        integrationPlatform = "CircuitOS"
        integrationVersion = "1.1.1"
        dataPath = $DataPath
        profileConfigured = [IO.File]::Exists($ProfilePath)
        actions = $actions
        checklist = @(
            "Create one Streamer.bot action for each generated code block.",
            "Add an Execute C# sub-action, replace its contents, and compile.",
            "For Redemption, confirm Newtonsoft.Json and Microsoft.CSharp on the References tab.",
            "Attach the listed Twitch reward or command triggers.",
            "Run !components, !scrap, and one test redemption.",
            "Confirm inventory.json and overlay\overlay-state.json update in the data folder."
        )
    }
}

function Get-BackupTargetDefinition {
    param([string]$FileName)

    if ($FileName -match '^components_\d{8}_\d{6}_\d{3}\.json$') {
        return [pscustomobject]@{ key = "components"; label = "Components Catalog"; path = $ComponentsPath; backupLabel = "components" }
    }
    if ($FileName -match '^featured-boost_\d{8}_\d{6}_\d{3}\.json$') {
        return [pscustomobject]@{ key = "boost"; label = "Featured Boost"; path = $BoostPath; backupLabel = "featured-boost" }
    }
    if ($FileName -match '^discord-role-awards_\d{8}_\d{6}_\d{3}\.json$') {
        return [pscustomobject]@{ key = "roles"; label = "Discord Role Awards"; path = $RoleAwardsPath; backupLabel = "discord-role-awards" }
    }
    if ($FileName -match '^system-profile_\d{8}_\d{6}_\d{3}\.json$') {
        return [pscustomobject]@{ key = "profile"; label = "System Profile"; path = $ProfilePath; backupLabel = "system-profile" }
    }
    return $null
}

function Get-BackupCenter {
    $targets = @(
        [pscustomobject]@{ key = "components"; label = "Components Catalog"; path = $ComponentsPath },
        [pscustomobject]@{ key = "boost"; label = "Featured Boost"; path = $BoostPath },
        [pscustomobject]@{ key = "roles"; label = "Discord Role Awards"; path = $RoleAwardsPath }
        [pscustomobject]@{ key = "profile"; label = "System Profile"; path = $ProfilePath }
    )
    $liveFiles = foreach ($target in $targets) {
        $exists = [IO.File]::Exists($target.path)
        $info = if ($exists) { [IO.FileInfo]$target.path } else { $null }
        [pscustomobject]@{
            key = $target.key
            label = $target.label
            exists = $exists
            size = if ($exists) { $info.Length } else { 0 }
            modifiedAtUtc = if ($exists) { $info.LastWriteTimeUtc.ToString("o") } else { "" }
        }
    }

    $backups = New-Object System.Collections.Generic.List[object]
    if ([IO.Directory]::Exists($BackupPath)) {
        foreach ($file in @([IO.Directory]::GetFiles($BackupPath, "*.json"))) {
            $info = [IO.FileInfo]$file
            $target = Get-BackupTargetDefinition $info.Name
            if ($null -eq $target) { continue }
            $backups.Add([pscustomobject]@{
                fileName = $info.Name
                targetKey = $target.key
                targetLabel = $target.label
                size = $info.Length
                createdAtUtc = $info.LastWriteTimeUtc.ToString("o")
            })
        }
    }

    return [pscustomobject]@{
        liveFiles = $liveFiles
        backups = @($backups | Sort-Object createdAtUtc -Descending)
        backupPath = $BackupPath
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
}

function Resolve-BackupFile {
    param([string]$FileName)

    if ([string]::IsNullOrWhiteSpace($FileName) -or [IO.Path]::GetFileName($FileName) -ne $FileName) {
        throw "Invalid backup filename."
    }
    $target = Get-BackupTargetDefinition $FileName
    if ($null -eq $target) { throw "This file is not a managed configuration backup." }
    $backupRoot = [IO.Path]::GetFullPath($BackupPath).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath((Join-Path $BackupPath $FileName))
    if (-not $path.StartsWith($backupRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Backup path escaped the managed backup folder." }
    if (-not [IO.File]::Exists($path)) { throw "Backup file was not found." }
    return [pscustomobject]@{ path = $path; target = $target; info = [IO.FileInfo]$path }
}

function Test-RoleAwardsState {
    param($State)
    $errors = New-Object System.Collections.Generic.List[string]
    if ($null -eq (Get-PropertyValue $State "roleNames")) { $errors.Add("Role award state needs roleNames.") }
    if ($null -eq (Get-PropertyValue $State "fulfilled")) { $errors.Add("Role award state needs fulfilled acknowledgements.") }
    return $errors
}

function Test-BackupContent {
    param($Target, $Content)

    if ($Target.key -eq "components") {
        $boost = if ([IO.File]::Exists($BoostPath)) { Read-JsonFile $BoostPath } else { Get-DefaultBoost }
        return @(Test-Configuration $Content $boost)
    }
    if ($Target.key -eq "boost") {
        $components = Read-JsonFile $ComponentsPath
        return @(Test-Configuration $components $Content)
    }
    if ($Target.key -eq "roles") { return @(Test-RoleAwardsState $Content) }
    if ($Target.key -eq "profile") { return @(Test-SystemProfile $Content) }
    return @("Unknown backup target.")
}

function Invoke-BackupOperation {
    param($RequestBody)

    $operation = [string](Get-PropertyValue $RequestBody "operation" "")
    $fileName = [string](Get-PropertyValue $RequestBody "fileName" "")
    try { $resolved = Resolve-BackupFile $fileName }
    catch { return [pscustomobject]@{ ok = $false; status = 400; errors = @($_.Exception.Message) } }

    try { $content = Read-JsonFile $resolved.path }
    catch {
        if ($operation -eq "preview") {
            return [pscustomobject]@{
                ok = $true
                status = 200
                file = [pscustomobject]@{
                    fileName = $resolved.info.Name
                    targetKey = $resolved.target.key
                    targetLabel = $resolved.target.label
                    size = $resolved.info.Length
                    createdAtUtc = $resolved.info.LastWriteTimeUtc.ToString("o")
                }
                content = $null
                liveContent = if ([IO.File]::Exists($resolved.target.path)) { Read-JsonFile $resolved.target.path } else { $null }
                validationErrors = @("Backup JSON could not be parsed: $($_.Exception.Message)")
            }
        }
        return [pscustomobject]@{ ok = $false; status = 400; errors = @("Backup JSON could not be parsed: $($_.Exception.Message)") }
    }
    $errors = @(Test-BackupContent $resolved.target $content)
    if ($operation -eq "preview") {
        $liveContent = if ([IO.File]::Exists($resolved.target.path)) { Read-JsonFile $resolved.target.path } else { $null }
        return [pscustomobject]@{
            ok = $true
            status = 200
            file = [pscustomobject]@{
                fileName = $resolved.info.Name
                targetKey = $resolved.target.key
                targetLabel = $resolved.target.label
                size = $resolved.info.Length
                createdAtUtc = $resolved.info.LastWriteTimeUtc.ToString("o")
            }
            content = $content
            liveContent = $liveContent
            validationErrors = $errors
        }
    }
    if ($operation -ne "restore") {
        return [pscustomobject]@{ ok = $false; status = 400; errors = @("Unknown backup operation.") }
    }
    if ($errors.Count -gt 0) {
        return [pscustomobject]@{ ok = $false; status = 400; errors = $errors }
    }

    $timestamp = [DateTime]::Now.ToString("yyyyMMdd_HHmmss_fff")
    $preRestoreBackup = Write-AtomicJson $resolved.target.path $content $resolved.target.backupLabel $timestamp
    return [pscustomobject]@{
        ok = $true
        status = 200
        restoredFile = $resolved.info.Name
        target = $resolved.target.label
        restoredAtUtc = [DateTime]::UtcNow.ToString("o")
        preRestoreBackup = $preRestoreBackup
    }
}

function Save-Configuration {
    param($RequestBody)

    $components = Get-PropertyValue $RequestBody "components"
    $boost = Get-PropertyValue $RequestBody "boost"
    $errors = @(Test-Configuration $components $boost)

    if ($errors.Count -gt 0) {
        return [pscustomobject]@{ ok = $false; status = 400; errors = $errors }
    }

    $timestamp = [DateTime]::Now.ToString("yyyyMMdd_HHmmss_fff")
    $componentBackup = Write-AtomicJson $ComponentsPath $components "components" $timestamp
    $boostBackup = Write-AtomicJson $BoostPath $boost "featured-boost" $timestamp

    return [pscustomobject]@{
        ok = $true
        status = 200
        savedAtUtc = [DateTime]::UtcNow.ToString("o")
        backups = @($componentBackup, $boostBackup) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
}

function Read-HttpRequest {
    param([Net.Sockets.TcpClient]$Client)

    $stream = $Client.GetStream()
    $headerBytes = New-Object System.Collections.Generic.List[byte]
    $window = New-Object System.Collections.Generic.Queue[byte]

    while ($true) {
        $next = $stream.ReadByte()
        if ($next -lt 0) { throw "Connection closed before request headers completed." }
        $headerBytes.Add([byte]$next)
        $window.Enqueue([byte]$next)
        while ($window.Count -gt 4) { $null = $window.Dequeue() }

        if ($window.Count -eq 4) {
            $bytes = $window.ToArray()
            if ($bytes[0] -eq 13 -and $bytes[1] -eq 10 -and $bytes[2] -eq 13 -and $bytes[3] -eq 10) { break }
        }

        if ($headerBytes.Count -gt 32768) { throw "Request headers are too large." }
    }

    $headerText = [Text.Encoding]::ASCII.GetString($headerBytes.ToArray())
    $lines = $headerText -split "`r`n"
    $requestParts = $lines[0] -split ' '
    if ($requestParts.Count -lt 2) { throw "Invalid request line." }

    $headers = @{}
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $separator = $lines[$i].IndexOf(':')
        if ($separator -gt 0) {
            $headers[$lines[$i].Substring(0, $separator).Trim().ToLowerInvariant()] = $lines[$i].Substring($separator + 1).Trim()
        }
    }

    $contentLength = 0
    if ($headers.ContainsKey("content-length")) { $contentLength = [int]$headers["content-length"] }
    if ($contentLength -lt 0 -or $contentLength -gt 1048576) { throw "Request body is too large." }
    $bodyBytes = New-Object byte[] $contentLength
    $offset = 0

    while ($offset -lt $contentLength) {
        $read = $stream.Read($bodyBytes, $offset, $contentLength - $offset)
        if ($read -le 0) { throw "Connection closed before request body completed." }
        $offset += $read
    }

    return [pscustomobject]@{
        Method = $requestParts[0].ToUpperInvariant()
        Path = ($requestParts[1] -split '\?')[0]
        Body = [Text.Encoding]::UTF8.GetString($bodyBytes)
    }
}

function Send-HttpResponse {
    param([Net.Sockets.TcpClient]$Client, [int]$Status, [string]$ContentType, [byte[]]$Body)

    $reason = switch ($Status) { 200 { "OK" } 400 { "Bad Request" } 404 { "Not Found" } default { "Internal Server Error" } }
    $headers = "HTTP/1.1 $Status $reason`r`nContent-Type: $ContentType`r`nContent-Length: $($Body.Length)`r`nCache-Control: no-store`r`nX-Content-Type-Options: nosniff`r`nConnection: close`r`n`r`n"
    $headerBytes = [Text.Encoding]::ASCII.GetBytes($headers)
    $stream = $Client.GetStream()
    $stream.Write($headerBytes, 0, $headerBytes.Length)
    $stream.Write($Body, 0, $Body.Length)
    $stream.Flush()
}

function Send-JsonResponse {
    param([Net.Sockets.TcpClient]$Client, [int]$Status, $Value)
    $json = $Value | ConvertTo-Json -Depth 100 -Compress
    Send-HttpResponse $Client $Status "application/json; charset=utf-8" ([Text.Encoding]::UTF8.GetBytes($json))
}

function Send-StaticFile {
    param([Net.Sockets.TcpClient]$Client, [string]$RequestPath)

    $fileMap = @{
        "/" = @("index.html", "text/html; charset=utf-8")
        "/index.html" = @("index.html", "text/html; charset=utf-8")
        "/styles.css" = @("styles.css", "text/css; charset=utf-8")
        "/app.js" = @("app.js", "application/javascript; charset=utf-8")
    }

    if (-not $fileMap.ContainsKey($RequestPath)) {
        Send-HttpResponse $Client 404 "text/plain; charset=utf-8" ([Text.Encoding]::UTF8.GetBytes("Not found"))
        return
    }

    $entry = $fileMap[$RequestPath]
    $path = Join-Path $UiPath $entry[0]
    Send-HttpResponse $Client 200 $entry[1] ([IO.File]::ReadAllBytes($path))
}

if (-not [IO.File]::Exists($ComponentsPath)) {
    throw "components.json was not found at $ComponentsPath"
}

$listener = New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $Port
$listener.Start()
$url = "http://127.0.0.1:$Port/"
Write-Host "CircuitOS is running at $url" -ForegroundColor Cyan
Write-Host "Editing live configuration in: $DataPath" -ForegroundColor DarkGray
Write-Host "Close this window or press Ctrl+C to stop." -ForegroundColor DarkGray

if (-not $NoBrowser) {
    Start-Process $url
}

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()

        try {
            $request = Read-HttpRequest $client

            if ($request.Method -eq "GET" -and $request.Path -eq "/api/health") {
                Send-JsonResponse $client 200 ([pscustomobject]@{ ok = $true; dataPath = $DataPath })
            }
            elseif ($request.Method -eq "GET" -and $request.Path -eq "/api/config") {
                Send-JsonResponse $client 200 (Get-Configuration)
            }
            elseif ($request.Method -eq "GET" -and $request.Path -eq "/api/profile") {
                Send-JsonResponse $client 200 (Get-SystemProfile)
            }
            elseif ($request.Method -eq "GET" -and $request.Path -eq "/api/overlay-config") {
                Send-JsonResponse $client 200 (Get-OverlayConfig)
            }
            elseif ($request.Method -eq "POST" -and $request.Path -eq "/api/profile") {
                try {
                    $body = $request.Body | ConvertFrom-Json
                    $result = Save-SystemProfile $body
                    Send-JsonResponse $client $result.status $result
                }
                catch {
                    Send-JsonResponse $client 500 ([pscustomobject]@{ ok = $false; errors = @($_.Exception.Message) })
                }
            }
            elseif ($request.Method -eq "POST" -and $request.Path -eq "/api/overlay-config") {
                try {
                    $result = Save-OverlayConfig ($request.Body | ConvertFrom-Json)
                    Send-JsonResponse $client $result.status $result
                }
                catch { Send-JsonResponse $client 500 ([pscustomobject]@{ ok = $false; errors = @($_.Exception.Message) }) }
            }
            elseif ($request.Method -eq "POST" -and $request.Path -eq "/api/setup") {
                try {
                    $body = $request.Body | ConvertFrom-Json
                    $profile = Get-PropertyValue $body "profile"
                    $result = Get-StreamerBotSetup $profile
                    Send-JsonResponse $client $result.status $result
                }
                catch {
                    Send-JsonResponse $client 500 ([pscustomobject]@{ ok = $false; errors = @($_.Exception.Message) })
                }
            }
            elseif ($request.Method -eq "GET" -and $request.Path -eq "/api/analytics") {
                Send-JsonResponse $client 200 (Get-InventoryAnalytics)
            }
            elseif ($request.Method -eq "GET" -and $request.Path -eq "/api/roles") {
                Send-JsonResponse $client 200 (Get-DiscordRoleAwards)
            }
            elseif ($request.Method -eq "GET" -and $request.Path -eq "/api/backups") {
                Send-JsonResponse $client 200 (Get-BackupCenter)
            }
            elseif ($request.Method -eq "POST" -and $request.Path -eq "/api/backups") {
                try {
                    $body = $request.Body | ConvertFrom-Json
                    $result = Invoke-BackupOperation $body
                    Send-JsonResponse $client $result.status $result
                }
                catch {
                    Send-JsonResponse $client 500 ([pscustomobject]@{ ok = $false; errors = @($_.Exception.Message) })
                }
            }
            elseif ($request.Method -eq "POST" -and $request.Path -eq "/api/roles") {
                try {
                    $body = $request.Body | ConvertFrom-Json
                    $result = Update-DiscordRoleAwards $body
                    Send-JsonResponse $client $result.status $result
                }
                catch {
                    Send-JsonResponse $client 500 ([pscustomobject]@{ ok = $false; errors = @($_.Exception.Message) })
                }
            }
            elseif ($request.Method -eq "POST" -and $request.Path -eq "/api/save") {
                try {
                    $body = $request.Body | ConvertFrom-Json
                    $result = Save-Configuration $body
                    Send-JsonResponse $client $result.status $result
                }
                catch {
                    Send-JsonResponse $client 500 ([pscustomobject]@{ ok = $false; errors = @($_.Exception.Message) })
                }
            }
            else {
                Send-StaticFile $client $request.Path
            }
        }
        catch {
            try { Send-JsonResponse $client 500 ([pscustomobject]@{ ok = $false; errors = @($_.Exception.Message) }) } catch { }
        }
        finally {
            $client.Close()
        }
    }
}
finally {
    $listener.Stop()
}
