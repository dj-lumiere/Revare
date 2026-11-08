# @LANGUAGE@ installer (Windows)
#
# Adds this package folder to your user PATH so `@TOOL@` and `@ALIAS@` work from any terminal.
# Everything runs from this folder: no admin rights, nothing in the registry beyond the user PATH
# entry, no other downloads.
#
# Usage:  install.cmd
# Or directly from PowerShell 5.1+:
#   powershell -ExecutionPolicy Bypass -File .\install.ps1
$ErrorActionPreference = 'Stop'

$dir = $PSScriptRoot
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
# @( ) keeps a one-entry PATH an array, so adding a folder appends it instead of gluing the text together.
$entries = @($userPath -split ';' | Where-Object { $_ -ne '' })

if ($entries -contains $dir) {
    Write-Host "Already installed: $dir is on your user PATH."
} else {
    [Environment]::SetEnvironmentVariable('Path', (($entries + $dir) -join ';'), 'User')
    Write-Host "Added to user PATH: $dir"
    Write-Host 'Open a NEW terminal for the change to take effect.'
}

# Windows runs the first @EXE@.exe it finds on PATH (the machine PATH comes before the user PATH).
# Another folder with @EXE@.exe that comes before this one wins, so say which one.
$searched = (($machinePath -split ';') + ($entries + $dir)) | Where-Object { $_ -ne '' }
$found = $searched | Where-Object { Test-Path (Join-Path $_ '@EXE@.exe') } | Select-Object -Unique
$first = $found | Select-Object -First 1
if ($first -and ($first.TrimEnd('\') -ne $dir.TrimEnd('\'))) {
    Write-Host ''
    Write-Host "Warning: '@TOOL@' will run $first\@EXE@.exe, not the one in this folder."
    Write-Host "That folder comes earlier on PATH. Remove it from PATH to use this package."
}

Write-Host ''
Write-Host 'Try it:'
Write-Host '  @TOOL@ version'
Write-Host '  @TOOL@ run hello.@EXT@      (@ALIAS@ works too)'
Write-Host ''
Write-Host 'See @GUIDE@ for a hello-world walkthrough.'
