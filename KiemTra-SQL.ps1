param([switch]$ValidateOnly)
$ErrorActionPreference = 'Continue'
function New-LabConnectionString([string]$ServerName) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $ServerName
    $builder['Initial Catalog'] = 'master'
    $builder['Integrated Security'] = $true
    $builder['Connect Timeout'] = 5
    return $builder.ConnectionString
}
if ($ValidateOnly) {
    $ErrorActionPreference = 'Stop'
    foreach ($testServer in @('.\SQLEXPRESS', '(localdb)\MSSQLLocalDB')) {
        $parsed = New-Object System.Data.SqlClient.SqlConnectionStringBuilder (New-LabConnectionString $testServer)
        if ($parsed['Data Source'] -ne $testServer -or $parsed['Initial Catalog'] -ne 'master' -or $parsed['Connect Timeout'] -ne 5 -or $parsed['Integrated Security'] -ne $true) {
            throw 'Unexpected connection string values.'
        }
        Write-Host "PASS connection string: $testServer"
    }
    return
}
$outputDirectory = Join-Path $PSScriptRoot ('SQL-ChanDoan-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $outputDirectory | Out-Null
Start-Transcript -Path (Join-Path $outputDirectory 'diagnostic.txt') | Out-Null
try {
    Write-Host 'SQL services:'
    Get-Service | Where-Object { $_.Name -like 'MSSQL*' -or $_.Name -eq 'SQLBrowser' } |
        Format-Table Name, Status -AutoSize | Out-Host
    $service = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Stopped') {
        Write-Host 'Starting the installed SQLEXPRESS service...'
        try { Start-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop }
        catch { Write-Host ('Cannot start SQL service: ' + $_.Exception.Message) }
    }
    $servers = @('.\SQLEXPRESS')
    $instanceKey = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -ErrorAction SilentlyContinue
    if ($instanceKey) {
        foreach ($property in $instanceKey.PSObject.Properties) {
            if ($property.Name -notlike 'PS*') {
                if ($property.Name -eq 'MSSQLSERVER') { $servers += '.' }
                else { $servers += ('.\' + $property.Name) }
            }
        }
    }
    $servers += '(localdb)\MSSQLLocalDB'
    foreach ($server in ($servers | Select-Object -Unique)) {
        Write-Host "Checking SQL instance: $server"
        $connection = $null
        try {
            $connection = New-Object System.Data.SqlClient.SqlConnection (New-LabConnectionString $server)
            $connection.Open()
            Write-Host "SQL_CONNECTION_OK: $server" -ForegroundColor Green
            $command = $connection.CreateCommand()
            $command.CommandText = 'SELECT @@SERVERNAME'
            Write-Host ('Server name: ' + $command.ExecuteScalar())
            $server | Set-Content -LiteralPath (Join-Path $outputDirectory 'working-instance.txt') -Encoding UTF8
            break
        } catch { Write-Host ('SQL_CONNECTION_FAILED: ' + $_.Exception.Message) }
        finally { if ($connection) { $connection.Dispose() } }
    }
    Write-Host "Log folder: $outputDirectory"
} finally { Stop-Transcript | Out-Null }
