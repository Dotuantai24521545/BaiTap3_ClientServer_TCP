$ErrorActionPreference = 'Stop'
$buildRoot = $PSScriptRoot
$buildCommand = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
if ($buildCommand) { $buildMsbuild = $buildCommand.Source } else {
    $buildVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $buildVswhere)) { throw 'Open Developer PowerShell for Visual Studio and retry.' }
    $buildMsbuild = & $buildVswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $buildMsbuild) { throw 'MSBuild was not found.' }
}
& $buildMsbuild (Join-Path $buildRoot 'BaiTap3_QuanLyNguoiDung_TCP.sln') /t:Build /p:Configuration=Release /p:UseSharedCompilation=false /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Close any running EXEs of this solution before rebuilding.' }
foreach ($component in @('Client','Server','Tests')) {
    $destination = Join-Path $buildRoot ('ChayThu\' + $component)
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $buildRoot ('UserManagement.'+$component+'\bin\Release')) -File |
        Where-Object { $_.Extension -in @('.exe','.dll','.config') } | Copy-Item -Destination $destination
}
Write-Host 'Build succeeded. Start server first, then run two copies of the client.'
