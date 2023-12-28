[CmdletBinding()]
param (
    [Parameter()]
    [string]
    [ValidateSet("Release", "Debug")]
    $Configuration = "Debug",
    
    [Parameter()]
    [switch]
    $NoRestore
)

. "$PSScriptRoot\common.ps1"

$msBuildPath = Resolve-MsBuildPath -ErrorAction Stop

if (!$msBuildPath) {
    throw "Could not resolve MSBuild path."
}

$null = Test-Path $msBuildPath -ErrorAction Stop

$solution = Join-Path -Path $PSScriptRoot -ChildPath 'TwinGet.sln'

if (-not $NoRestore) {
    dotnet restore $solution
}

& $msBuildPath $solution -p:Configuration=$Configuration
