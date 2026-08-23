# Build a Release zip, regenerate repo.json, push it, and upload the zip to a GitHub release.
# Usage: .\scripts\publish-testing.ps1 [-Version 1.0.0.1]  (omit -Version to reuse the csproj version)
param([string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$csproj = 'src\Wikiway.Plugin\Wikiway.Plugin.csproj'
if ($Version)
{
    $text = Get-Content $csproj -Raw
    $text = $text -replace '<Version>[\d\.]+</Version>', "<Version>$Version</Version>"
    Set-Content $csproj $text -Encoding utf8 -NoNewline
}

dotnet build $csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$outDir = 'src\Wikiway.Plugin\bin\Release\Wikiway'
$entry = Get-Content (Join-Path $outDir 'Wikiway.json') -Raw | ConvertFrom-Json
$ver = $entry.AssemblyVersion

# releases/latest/download always resolves to the newest release, so repo.json never needs a new URL.
$zipUrl = 'https://github.com/azam997/Wikiway/releases/latest/download/latest.zip'
foreach ($link in 'DownloadLinkInstall', 'DownloadLinkTesting', 'DownloadLinkUpdate')
{
    $entry | Add-Member -NotePropertyName $link -NotePropertyValue $zipUrl -Force
}
$entry | Add-Member -NotePropertyName 'IsHide' -NotePropertyValue $false -Force
$entry | Add-Member -NotePropertyName 'IsTestingExclusive' -NotePropertyValue $false -Force
$entry | Add-Member -NotePropertyName 'TestingAssemblyVersion' -NotePropertyValue $ver -Force
$entry | Add-Member -NotePropertyName 'LastUpdate' -NotePropertyValue ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) -Force
ConvertTo-Json -InputObject @($entry) -Depth 8 | Set-Content repo.json -Encoding utf8

git add repo.json $csproj
git diff --cached --quiet
if (-not $?)
{
    git commit -m "Publish testing build $ver"
    git push
}

$tag = "v$ver"
$existingTags = gh release list --json tagName --jq '.[].tagName'
if ($existingTags -contains $tag)
{
    gh release upload $tag (Join-Path $outDir 'latest.zip') --clobber
}
else
{
    gh release create $tag (Join-Path $outDir 'latest.zip') --title "Wikiway $ver" --notes "Testing build. Install via the custom repo: see docs\testing-install.md." --latest
}
Write-Host "Published $ver. Repo URL for Dalamud: https://raw.githubusercontent.com/azam997/Wikiway/master/repo.json"
