param([string]$RepositorySlug = $env:GITHUB_REPOSITORY)

$ErrorActionPreference = "Stop"
if ($RepositorySlug -and $RepositorySlug -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "RepositorySlug must use the owner/repository format"
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$versionInfo = Get-Content -LiteralPath (Join-Path $projectRoot "version.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string]$versionInfo.version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid version.json version" }
$moduleId = "mediaforge_jellyfin_connector"
$source = Join-Path $projectRoot "MediaForge.Module/$moduleId"
$metadata = Get-Content -LiteralPath (Join-Path $source "__init__.py") -Raw -Encoding UTF8
function Read-ModuleString([string]$Name) {
    $match = [regex]::Match($metadata, ('(?m)^' + [regex]::Escape($Name) + ' = "([^"]*)"'))
    if (-not $match.Success) { throw "Missing module metadata: $Name" }
    return $match.Groups[1].Value
}
if ((Read-ModuleString "MODULE_VERSION") -ne $version -or (Read-ModuleString "MODULE_ID") -ne $moduleId) {
    throw "Module identity/version does not match release metadata"
}
$store = Join-Path $projectRoot "module-store"
$packages = Join-Path $store "packages"
New-Item -ItemType Directory -Path $packages -Force | Out-Null
$packageName = "$moduleId-$version.mfmod"
$packagePath = Join-Path $packages $packageName
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($packagePath, [IO.FileMode]::Create)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $source -File -Filter "*.py" | Sort-Object Name)) {
            $entry = $archive.CreateEntry("$moduleId/$($file.Name)", [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $inputStream = [IO.File]::OpenRead($file.FullName)
            try {
                $entryStream = $entry.Open()
                try { $inputStream.CopyTo($entryStream) } finally { $entryStream.Dispose() }
            } finally { $inputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$homepage = if ($RepositorySlug) { "https://github.com/$RepositorySlug" } else { "" }
$sourceUrl = if ($homepage) { "$homepage/tree/main/MediaForge.Module/$moduleId" } else { "" }
$index = [ordered]@{
    store_api = 1
    name = "MediaForge Jellyfin Connector"
    modules = @([ordered]@{
        id = $moduleId
        folder = $moduleId
        type = "module"
        name = (Read-ModuleString "MODULE_NAME")
        version = $version
        author = (Read-ModuleString "MODULE_AUTHOR")
        trust = "unverified"
        description = [ordered]@{
            de = "Jellyfin-Anfragen, sichere Downloadübergaben und Autosync nach Serienfreigabe."
            en = "Jellyfin requests, durable download handoffs and AutoSync after series approval."
        }
        api_version = 1
        min_app_version = (Read-ModuleString "MODULE_MIN_APP_VERSION")
        max_app_version = (Read-ModuleString "MODULE_MAX_APP_VERSION")
        requirements = @()
        homepage = $homepage
        repo_url = $homepage
        source_url = $sourceUrl
        license = (Read-ModuleString "MODULE_LICENSE")
        download_url = "packages/$packageName"
        sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        size = (Get-Item -LiteralPath $packagePath).Length
    })
}
$json = (ConvertTo-Json -InputObject $index -Depth 8) + [Environment]::NewLine
# Some MediaForge versions request index-all.json when unverified modules are allowed.
foreach ($name in @("index.json", "index-all.json")) {
    [IO.File]::WriteAllText((Join-Path $store $name), $json, [Text.UTF8Encoding]::new($false))
}
Write-Output "Created module store: $store (v$version, unverified)"
