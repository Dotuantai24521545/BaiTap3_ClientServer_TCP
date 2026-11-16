param([string]$SqlInstance = '.\SQLEXPRESS')
$ErrorActionPreference = 'Stop'
$outputDirectory = Join-Path $PSScriptRoot ('MinhChung-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $outputDirectory | Out-Null
('Time: ' + (Get-Date).ToString('o')) | Set-Content (Join-Path $outputDirectory 'machine.txt') -Encoding UTF8
$testExe = Join-Path $PSScriptRoot 'ChayThu\Tests\UserManagement.Tests.exe'
& $testExe $outputDirectory --network
$testCode = $LASTEXITCODE
Write-Host "Test process exit code: $testCode"
Write-Host 'TCP tests use a test repository; real SQL must also be verified.'
$sqlCommand = Get-Command sqlcmd -ErrorAction SilentlyContinue
if ($sqlCommand) {
    $ErrorActionPreference = 'Continue'
    & $sqlCommand.Source -S $SqlInstance -E -l 5 -Q 'SELECT @@VERSION AS SqlVersion; SELECT name FROM sys.databases;' 2>&1 |
        Tee-Object -FilePath (Join-Path $outputDirectory 'sql-probe.txt')
    $ErrorActionPreference = 'Stop'
} else { 'sqlcmd unavailable: use SSMS to verify SQL.' | Set-Content (Join-Path $outputDirectory 'sql-probe.txt') }
Write-Host "Evidence folder: $outputDirectory"
Write-Host 'Next: follow TaiLieu/CHECKLIST_CHAY_THAT.md for real SQL and live UI evidence.'
