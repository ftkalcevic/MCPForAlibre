<#
.SYNOPSIS
    Builds and installs MCPForAlibre: the add-on into Alibre Design and the MCP server beside it.

.DESCRIPTION
    1. Builds the add-on (net48) and publishes the MCP server (net9.0) into out\<Configuration>\.
    2. Copies MCPForAlibre.AddOn.dll and MCPForAlibre.adc to
       %ProgramData%\Alibre AddOns\MCPForAlibre\
       and the server to a server\ folder there.
    3. Registers the add-on under HKLM\SOFTWARE\Alibre Design Add-Ons
       (value name = add-on GUID, data = install folder).

    Steps 2 and 3 need administrator rights; the script re-launches itself elevated for them.
    Close Alibre Design first (the DLL is locked while Alibre runs), and restart it
    afterwards - Alibre only reads add-ons at startup. Close Claude's MCP connection too
    (or restart Claude), as the running server locks its own files.

.PARAMETER Configuration
    Build configuration to install: Release (default) or Debug.
#>
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$AddOnName = 'MCPForAlibre'
$AddOnGuid = '{5374DA87-716D-4EAB-ABA8-661FE072285D}'   # must match MCPForAlibre.adc
$RegKey    = 'SOFTWARE\Alibre Design Add-Ons'

$projectDir = Split-Path -Parent $PSScriptRoot
$outDir     = Join-Path $projectDir "out\$Configuration"

# --- Build (as the user, before elevating) --------------------------------------
if (-not $SkipBuild) {
    dotnet build (Join-Path $projectDir 'src\MCPForAlibre.AddOn') -c $Configuration -o (Join-Path $outDir 'addon')
    if ($LASTEXITCODE -ne 0) { throw 'The add-on failed to build.' }
    dotnet publish (Join-Path $projectDir 'src\MCPForAlibre.Server') -c $Configuration -o (Join-Path $outDir 'server')
    if ($LASTEXITCODE -ne 0) { throw 'The server failed to build.' }
}

# --- Elevation ---------------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Administrator rights are required - relaunching elevated...'
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
                 '-Configuration', $Configuration, '-SkipBuild')
    Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait
    exit
}

try {
    $addonFiles = @("$AddOnName.AddOn.dll", "$AddOnName.adc", "$AddOnName.ico")
    foreach ($f in $addonFiles) {
        if (-not (Test-Path (Join-Path $outDir "addon\$f"))) {
            throw "Missing '$f' in '$outDir\addon'. Run this script without -SkipBuild."
        }
    }
    if (-not (Test-Path (Join-Path $outDir 'server\MCPForAlibre.Server.exe'))) {
        throw "Missing the server in '$outDir\server'. Run this script without -SkipBuild."
    }

    # --- Copy files ------------------------------------------------------------
    # Alibre locks the add-on while it runs: then leave the add-on as it is and update only the server.
    $targetDir = Join-Path $env:ProgramData "Alibre AddOns\$AddOnName"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    if (Get-Process -Name 'Alibre Design' -ErrorAction SilentlyContinue) {
        Write-Host "Alibre Design is running, so the add-on wasn't replaced. Close Alibre and run this again to update it." -ForegroundColor Yellow
    }
    else {
        foreach ($f in $addonFiles) {
            Copy-Item -Path (Join-Path $outDir "addon\$f") -Destination $targetDir -Force
        }
        $pdb = Join-Path $outDir "addon\$AddOnName.AddOn.pdb"
        if (Test-Path $pdb) { Copy-Item -Path $pdb -Destination $targetDir -Force }
        Write-Host "Copied add-on files to $targetDir"
    }

    # The server is locked while a Claude session runs it. Then keep the installed copy, warn,
    # and still register the add-on.
    $serverDir = Join-Path $targetDir 'server'
    $running = Get-Process -Name 'MCPForAlibre.Server' -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "The MCP server is running (process $($running.Id -join ', ')), so its files weren't replaced." -ForegroundColor Yellow
        Write-Host 'Close the Claude sessions using it (or stop those processes) and run this again to update the server.' -ForegroundColor Yellow
    }
    else {
        if (Test-Path $serverDir) { Remove-Item -Path $serverDir -Recurse -Force }
        Copy-Item -Path (Join-Path $outDir 'server') -Destination $serverDir -Recurse -Force
        Write-Host "Copied the MCP server to $serverDir"
    }

    # --- Register --------------------------------------------------------------
    # Use the .NET API: New-Item -Force on HKLM:\SOFTWARE fails even when elevated.
    $key = [Microsoft.Win32.Registry]::LocalMachine.CreateSubKey($RegKey)
    try {
        $key.SetValue($AddOnGuid, $targetDir, [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally {
        $key.Close()
    }
    Write-Host "Registered $AddOnGuid -> $targetDir under HKLM\$RegKey"

    Write-Host ''
    Write-Host 'Done. Start Alibre Design; MCPForAlibre > Status shows the pipe.' -ForegroundColor Green
    Write-Host "Add the server to Claude Code with:"
    Write-Host "  claude mcp add --scope user alibre -- `"$serverDir\MCPForAlibre.Server.exe`""
}
catch {
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
}

if ($Host.Name -eq 'ConsoleHost') {
    Write-Host ''
    Read-Host 'Press Enter to close'
}
