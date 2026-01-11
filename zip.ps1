# Daily Bread source packaging script
# Creates a clean review zip without build artifacts or secrets

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$zipName = "Daily_Bread_Source_$timestamp.zip"
$zipPath = Join-Path (Get-Location) $zipName

# Items to include
$include = @(
    "Daily_Bread",
    "Daily_Bread.Tests",
    ".github",
    "public",
    "deploy.sh",
    "DESIGN_SYSTEM.md",
    "README.md",
    "SECURITY.md",
    "Dockerfile",
    "docker-compose.yml",
    "docker-compose.dev.yml",
    ".gitignore",
    ".gitattributes",
    "Daily_Bread.sln"
)

# Items to exclude everywhere
$excludeDirs = @(".git", ".vs")
$excludeFiles = @(".env")

# Create temp staging directory
$tempDir = Join-Path $env:TEMP "DailyBread_Package_$timestamp"
New-Item -ItemType Directory -Path $tempDir | Out-Null

Write-Host "Staging files..."

foreach ($item in $include) {
    if (Test-Path $item) {
        Copy-Item $item $tempDir -Recurse -Force
    } else {
        Write-Warning "Missing item: $item"
    }
}

# Remove excluded directories
foreach ($dir in $excludeDirs) {
    Get-ChildItem $tempDir -Recurse -Directory -Filter $dir -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
}

# Remove excluded files
foreach ($file in $excludeFiles) {
    Get-ChildItem $tempDir -Recurse -File -Filter $file -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

Write-Host "Creating zip: $zipName"
Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath -Force

# Cleanup
Remove-Item $tempDir -Recurse -Force

Write-Host "Done. Package created at:"
Write-Host $zipPath

