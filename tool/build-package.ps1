[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Runtime -ne "win-x64") {
    throw "0.1.0 packages only support the win-x64 runtime."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repositoryRoot "src/MushaAltSpaceIme/MushaAltSpaceIme.csproj"
$testProject = Join-Path $repositoryRoot "tests/MushaAltSpaceIme.Core.Tests/MushaAltSpaceIme.Core.Tests.csproj"
$readme = Join-Path $repositoryRoot "README.md"

foreach ($path in @($appProject, $testProject, $readme)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required package input was not found: $path"
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts"
}

if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagingDirectory = Join-Path $OutputDirectory "package-staging"
$publishDirectory = Join-Path $stagingDirectory "app"
$zipPath = Join-Path $OutputDirectory "musha-alt-space-ime-windows-x64.zip"
$checksumPath = "$zipPath.sha256"

Remove-Item -LiteralPath $stagingDirectory -Force -Recurse -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $checksumPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

& dotnet run -c $Configuration --project $testProject
if ($LASTEXITCODE -ne 0) {
    throw "Core tests failed with exit code $LASTEXITCODE."
}

& dotnet restore $appProject -r $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "Application restore failed with exit code $LASTEXITCODE."
}

& dotnet publish $appProject -c $Configuration -r $Runtime --self-contained true --no-restore -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $publishDirectory "musha-alt-space-ime.exe"
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Expected executable was not produced: $executable. Set the application AssemblyName to musha-alt-space-ime."
}

Copy-Item -LiteralPath $readme -Destination (Join-Path $stagingDirectory "README.md")
Copy-Item -LiteralPath (Join-Path $repositoryRoot ".chatgpt") -Destination (Join-Path $stagingDirectory ".chatgpt") -Recurse
$commit = "unavailable"
if (Get-Command git -ErrorAction SilentlyContinue) {
    $commit = (& git -C $repositoryRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $commit = "unavailable" }
}

@(
    "musha-alt-space-ime package",
    "runtime: $Runtime",
    "configuration: $Configuration",
    "source commit: $commit",
    "built at (UTC): $((Get-Date).ToUniversalTime().ToString('o'))",
    "This package is a Windows build artifact; it is not evidence of IME compatibility verification."
) | Set-Content -LiteralPath (Join-Path $stagingDirectory "SOURCE-INFO.txt") -Encoding utf8

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
# ZipFile also includes the .chatgpt documentation directory.
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDirectory, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  musha-alt-space-ime-windows-x64.zip" | Set-Content -LiteralPath $checksumPath -Encoding ascii
Remove-Item -LiteralPath $stagingDirectory -Force -Recurse

Write-Host "Created $zipPath"
Write-Host "SHA256: $hash"
