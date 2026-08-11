# Canary tests - run these after every FFXIV patch and after bumping Dalamud/Lumina versions.
#
#   GameData: reads the real game files, catches sheet/schema breakage. Skips if no install found.
#   WikiLive: hits the real wiki API, catches endpoint/shape changes. Skips on network-level failure,
#             FAILS on HTTP errors or shape mismatches (that's the signal the wiki changed).

$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..')

Write-Host "`n=== Game data canaries ===" -ForegroundColor Cyan
dotnet test tests\Wikiway.Canary.Tests --filter "Category=GameData" -v minimal
$gameData = $LASTEXITCODE

Write-Host "`n=== Wiki live contract tests ===" -ForegroundColor Cyan
dotnet test tests\Wikiway.Canary.Tests --filter "Category=WikiLive" -v minimal
$wikiLive = $LASTEXITCODE

Write-Host ""
Write-Host ("GameData: " + ($(if ($gameData -eq 0) { "OK" } else { "FAILED" })))
Write-Host ("WikiLive: " + ($(if ($wikiLive -eq 0) { "OK" } else { "FAILED" })))

if ($gameData -ne 0 -or $wikiLive -ne 0) {
    Write-Host "`nA canary failed. Something changed under us - see docs/patch-checklist.md" -ForegroundColor Yellow
    exit 1
}
exit 0
