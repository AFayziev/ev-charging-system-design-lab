#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$Detach,
    [switch]$Isolated
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$providers = Join-Path $repository 'simulators/EvCharging.Providers.AppHost/EvCharging.Providers.AppHost.csproj'
$platform = Join-Path $repository 'src/EvCharging.AppHost/EvCharging.AppHost.csproj'
$startedHosts = [System.Collections.Generic.List[string]]::new()
$previousEnvironment = @{}
$completed = $false

function Invoke-Aspire {
    param([string[]]$Arguments, [string]$AppHost)

    # Keep Aspire's local AppHost selection inside its owning directory.
    Push-Location ($AppHost ? (Split-Path -Parent $AppHost) : $repository)
    $previousConsoleEncoding = [Console]::OutputEncoding
    try {
        # PowerShell uses the console output encoding to decode native stdout.
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        $command = $Arguments[0]
        $commandArguments = $Arguments | Select-Object -Skip 1
        & aspire $command --non-interactive --nologo @commandArguments
        if ($LASTEXITCODE -ne 0) {
            throw "aspire $($Arguments[0]) failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        [Console]::OutputEncoding = $previousConsoleEncoding
        Pop-Location
    }
}

function Get-RunningAppHost {
    param([string]$AppHost)

    $running = Invoke-Aspire -Arguments @('ps', '--format', 'Json') | Out-String | ConvertFrom-Json
    $comparison = $IsWindows ? [StringComparison]::OrdinalIgnoreCase : [StringComparison]::Ordinal
    $matchingHosts = @($running | Where-Object {
        $path = [IO.Path]::GetFullPath($_.appHostPath, $repository)
        [string]::Equals($path, $AppHost, $comparison)
    })
    if ($matchingHosts.Count -gt 1) {
        throw "Multiple instances of '$AppHost' are running. Select one explicitly with the Aspire CLI."
    }
    return $matchingHosts | Select-Object -First 1
}

function Get-ResourceOrigin {
    param([string]$AppHost, [string]$Resource)

    $description = Invoke-Aspire -AppHost $AppHost -Arguments @(
        'describe', $Resource, '--apphost', $AppHost, '--format', 'Json'
    ) | Out-String | ConvertFrom-Json -AsHashtable
    $urls = @($description['resources'] | ForEach-Object { $_['urls'] } | Where-Object {
        -not $_['isInternal'] -and ([uri]$_['url']).Scheme -in @('http', 'https')
    })
    if ($urls.Count -ne 1) {
        throw "Expected one public HTTP endpoint for '$Resource'; found $($urls.Count)."
    }
    return ([uri]$urls[0]['url']).GetLeftPart([UriPartial]::Authority)
}

function Start-AppHost {
    param([string]$AppHost, [string[]]$ExtraArguments = @())

    $arguments = @('start', '--apphost', $AppHost)
    if ($Isolated) {
        $arguments += '--isolated'
    }
    $arguments += $ExtraArguments
    # Include partially started hosts in failure cleanup.
    $startedHosts.Add($AppHost)
    Invoke-Aspire -AppHost $AppHost -Arguments $arguments | Out-Host
}

function Wait-ForShutdown {
    $prompt = 'Press Enter or Ctrl+C to stop hosts started by this launcher'
    if ([Console]::IsInputRedirected) {
        Read-Host $prompt | Out-Null
        return
    }

    $previousControlCAsInput = [Console]::TreatControlCAsInput
    try {
        # Read Ctrl+C as a key so PowerShell does not cancel the cleanup pipeline.
        [Console]::TreatControlCAsInput = $true
        Write-Host $prompt
        do {
            $key = [Console]::ReadKey($true)
        } until ($key.Key -eq [ConsoleKey]::Enter -or $key.KeyChar -eq [char]3)
    }
    finally {
        [Console]::TreatControlCAsInput = $previousControlCAsInput
    }
}

try {
    $providerHost = Get-RunningAppHost $providers
    if ($providerHost) {
        Write-Host 'Reusing the provider AppHost.'
    }
    else {
        Start-AppHost $providers @('--', '--environment=Development')
    }

    $providerEndpoints = @{}
    foreach ($kind in @('synchronous', 'queued')) {
        $resource = "$kind-charging-provider-api"
        Invoke-Aspire -AppHost $providers -Arguments @(
            'wait', $resource, '--apphost', $providers, '--timeout', '120'
        ) | Out-Host
        $providerEndpoints[$kind] = Get-ResourceOrigin $providers $resource
    }

    $platformHost = Get-RunningAppHost $platform
    if ($platformHost) {
        foreach ($kind in $providerEndpoints.Keys) {
            $configured = Get-ResourceOrigin $platform "$kind-charging-provider-api"
            if ($configured -ne $providerEndpoints[$kind]) {
                throw 'The running platform uses different provider URLs. Stop its AppHost and rerun tools/dev.ps1.'
            }
        }
        Write-Host 'Reusing the platform AppHost.'
    }
    else {
        foreach ($kind in $providerEndpoints.Keys) {
            $name = "Parameters__$kind-provider-url"
            $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $providerEndpoints[$kind], 'Process')
        }
        Start-AppHost $platform @('--launch-profile', 'http')
    }

    Invoke-Aspire -AppHost $platform -Arguments @(
        'wait', 'frontend', '--apphost', $platform, '--timeout', '300'
    ) | Out-Host
    Invoke-Aspire -AppHost $platform -Arguments @(
        'describe', 'frontend', '--apphost', $platform
    ) | Out-Host
    $completed = $true

    if (-not $Detach) {
        Wait-ForShutdown
    }
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    if (-not $Detach -or -not $completed) {
        $cleanupFailed = $false
        for ($index = $startedHosts.Count - 1; $index -ge 0; $index--) {
            $appHost = $startedHosts[$index]
            try {
                Invoke-Aspire -AppHost $appHost -Arguments @('stop', '--apphost', $appHost) | Out-Host
            }
            catch {
                $cleanupFailed = $true
                Write-Warning "Could not stop '$appHost': $_"
            }
        }
        if ($completed -and $cleanupFailed) {
            throw 'Launcher cleanup failed. Stop the remaining AppHosts with the Aspire CLI.'
        }
    }
}
