# Fast unit tests. No network, no game install needed.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet test tests\Wikiway.Core.Tests -v minimal
exit $LASTEXITCODE
