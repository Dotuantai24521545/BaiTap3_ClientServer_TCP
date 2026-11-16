$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $PSScriptRoot ('SQL-MinhChung-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$testExe = Join-Path $PSScriptRoot 'ChayThu\Tests\UserManagement.Tests.exe'
Write-Host 'Running REAL SQL/TCP tests in a NEW isolated NT106_Bai3_Test_* database.'
Write-Host 'No existing application database is modified or deleted. Keep the evidence window open until it finishes.'
& $testExe $outputDirectory --sql "--root=$PSScriptRoot"
$testExitCode = $LASTEXITCODE
if ($testExitCode -eq 0) {
    $retainedEvidence = Join-Path $PSScriptRoot 'TaiLieu\MinhChung_SQL'
    New-Item -ItemType Directory -Force -Path $retainedEvidence | Out-Null
    Get-ChildItem -LiteralPath $outputDirectory -File | Copy-Item -Destination $retainedEvidence
}
Write-Host "Results folder: $outputDirectory"
Write-Host "SQL test exit code: $testExitCode"
if ($testExitCode -ne 0) { Write-Host 'Verification incomplete. The diagnostic log contains the actual failure.' -ForegroundColor Red }
