$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $PSScriptRoot ('UI-MinhChung-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$testExe = Join-Path $PSScriptRoot 'ChayThu\Tests\UserManagement.Tests.exe'
Write-Host 'Testing REAL UI Click/FormClosing handlers with real SQL/TCP, then a network blackhole simulation.'
Write-Host 'Wait about 1-2 minutes. No Wi-Fi, firewall, route or adapter settings are changed.'
& $testExe $outputDirectory --ui "--root=$PSScriptRoot"
Write-Host "UI test exit code: $LASTEXITCODE"
Write-Host "Evidence folder: $outputDirectory"
