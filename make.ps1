#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('help','all','clean','restore','build','build-library','build-collector','build-dashboard','test','format','lint','watch','publish-collector')]
    [string]$Target = 'help',
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$Sln = 'RimObs.sln'
$DashboardDir = 'RimObs.Dashboard'
$Rids = @('win-x64','linux-x64','osx-arm64','osx-x64')

function Show-Help {
    Write-Host "Targets: help, all, clean, restore, build, build-library, build-collector, build-dashboard, test, format, lint, watch, publish-collector"
}

function Invoke-Clean {
    Get-ChildItem -Path . -Recurse -Force -Include 'bin','obj' -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$DashboardDir/dist"
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue 'Assemblies/*.dll','Assemblies/*.pdb'
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue 'Harmony/Assemblies','Concord/Assemblies'
}

function Invoke-Restore {
    dotnet restore $Sln
    Push-Location $DashboardDir
    pnpm install
    Pop-Location
}

function Invoke-BuildDashboard {
    Push-Location $DashboardDir
    pnpm build
    Pop-Location
}

function Invoke-Build {
    Invoke-BuildDashboard
    dotnet build $Sln -c $Configuration --nologo
}

function Invoke-Test {
    dotnet test $Sln -c $Configuration --nologo --no-build
}

function Invoke-Format {
    dotnet format $Sln
    Push-Location $DashboardDir
    pnpm format
    Pop-Location
}

function Invoke-Lint {
    dotnet format $Sln --verify-no-changes
    Push-Location $DashboardDir
    pnpm lint
    Pop-Location
}

function Invoke-Watch {
    dotnet watch --project RimObs.Collector run -- serve
}

function Invoke-Publish {
    Invoke-BuildDashboard
    foreach ($rid in $Rids) {
        Write-Information "==> publish $rid" -InformationAction Continue
        dotnet publish RimObs.Collector/RimObs.Collector.csproj `
            -c Release -r $rid --self-contained true `
            -p:PublishSingleFile=true `
            -o "out/collector/$rid" `
            --nologo
    }
}

switch ($Target) {
    'help'              { Show-Help }
    'all'               { Invoke-Restore; Invoke-BuildDashboard; Invoke-Build }
    'clean'             { Invoke-Clean }
    'restore'           { Invoke-Restore }
    'build'             { Invoke-Build }
    'build-library'     { dotnet build RimObs.Library/RimObs.Library.csproj -c $Configuration --nologo }
    'build-collector'   { Invoke-BuildDashboard; dotnet build RimObs.Collector/RimObs.Collector.csproj -c $Configuration --nologo }
    'build-dashboard'   { Invoke-BuildDashboard }
    'test'              { Invoke-Test }
    'format'            { Invoke-Format }
    'lint'              { Invoke-Lint }
    'watch'             { Invoke-Watch }
    'publish-collector' { Invoke-Publish }
}
