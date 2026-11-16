param([switch]$Network)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1')
$testExe = Join-Path $PSScriptRoot 'UserManagement.Tests\bin\Release\UserManagement.Tests.exe'
$testOutput = Join-Path $PSScriptRoot 'artifacts'
if ($Network) { & $testExe $testOutput --network } else { & $testExe $testOutput }
if ($LASTEXITCODE -eq 1) { throw 'At least one test failed. See artifacts/test-results.txt.' }
if ($LASTEXITCODE -eq 2) { Write-Warning 'TCP integration tests were blocked or incomplete. See test-results.txt.' }
