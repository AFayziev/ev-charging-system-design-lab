#Requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$Detach,
    [switch]$Isolated,
    [switch]$Preview
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
    $ideEnvironment = @{}
    try {
        # Detached hosts belong to this launcher, not an inherited VS Code debug session.
        foreach ($name in @('ASPIRE_EXTENSION_ENDPOINT', 'DEBUG_SESSION_PORT')) {
            $ideEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        }
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
        foreach ($name in $ideEnvironment.Keys) {
            if ($null -eq $ideEnvironment[$name]) {
                Remove-Item "Env:$name" -ErrorAction SilentlyContinue
            }
            else {
                [Environment]::SetEnvironmentVariable($name, $ideEnvironment[$name], 'Process')
            }
        }
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

function Get-ResourceInstances {
    param([string]$AppHost, [string]$Resource)

    # Replicated resources are named '<resource>-<suffix>', which `aspire describe <resource>` does not resolve.
    $description = Invoke-Aspire -AppHost $AppHost -Arguments @(
        'describe', '--apphost', $AppHost, '--format', 'Json'
    ) | Out-String | ConvertFrom-Json -AsHashtable
    return @($description['resources'] |
        Where-Object { $_['name'] -match "^$([regex]::Escape($Resource))(-[a-z]{8})?$" })
}

function Get-ResourceOrigin {
    param([string]$AppHost, [string]$Resource)

    $origins = @(Get-ResourceInstances $AppHost $Resource |
        ForEach-Object { $_['urls'] } |
        Where-Object { -not $_['isInternal'] -and ([uri]$_['url']).Scheme -in @('http', 'https') } |
        ForEach-Object { ([uri]$_['url']).GetLeftPart([UriPartial]::Authority) } |
        Select-Object -Unique)
    if ($origins.Count -ne 1) {
        throw "Expected one public HTTP origin for '$Resource'; found $($origins.Count)."
    }
    return $origins[0]
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

function Get-WattHarborAuthentication {
    $description = Invoke-Aspire -AppHost $providers -Arguments @(
        'describe', 'wattharbor-provider-api', '--apphost', $providers, '--format', 'Json'
    ) | Out-String | ConvertFrom-Json -AsHashtable
    $authority = $description.resources[0].environment['ProviderAuthentication__Authority']
    if (-not $authority) {
        throw 'The WattHarbor authentication authority is unavailable.'
    }
    $metadata = Invoke-RestMethod "$($authority.TrimEnd('/'))/.well-known/openid-configuration" -TimeoutSec 10
    $secretJson = & dotnet user-secrets list --project $providers --json
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the provider AppHost credentials.'
    }
    $secrets = $secretJson | Where-Object { $_ -notmatch '^//' } | Out-String | ConvertFrom-Json -AsHashtable
    $clientSecret = $secrets['Parameters:charging-client-secret']
    $simulationClientSecret = $secrets['Parameters:simulation-client-secret']
    if (-not $clientSecret -or -not $simulationClientSecret -or -not $metadata.token_endpoint) {
        throw 'The provider token endpoint and the charging-client and simulation-client secrets are required.'
    }
    return @{
        TokenEndpoint = $metadata.token_endpoint
        ClientSecret = $clientSecret
        SimulationClientSecret = $simulationClientSecret
    }
}

function Register-WattHarborCallback {
    param([string]$ProviderOrigin, [hashtable]$Authentication, [string]$GatewayOrigin, [string]$CallbackKey)

    # The provider receives its callback target and key through its simulation control, never from the platform.
    $token = Invoke-RestMethod -Method Post -Uri $Authentication.TokenEndpoint -TimeoutSec 10 -Body @{
        grant_type = 'client_credentials'
        client_id = 'simulation-control'
        client_secret = $Authentication.SimulationClientSecret
    }
    $body = @{
        url = "$GatewayOrigin/api/callbacks/wattharbor/observations"
        apiKey = $CallbackKey
    } | ConvertTo-Json
    Invoke-RestMethod -Method Put -Uri "$ProviderOrigin/simulation/callbacks/observations" -TimeoutSec 10 `
        -Headers @{ Authorization = "Bearer $($token.access_token)" } -ContentType 'application/json' -Body $body |
        Out-Null
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
    foreach ($provider in @('wattharbor', 'amptrail')) {
        $resource = "$provider-provider-api"
        Invoke-Aspire -AppHost $providers -Arguments @(
            'wait', $resource, '--apphost', $providers, '--timeout', '120'
        ) | Out-Host
        $providerEndpoints[$provider] = Get-ResourceOrigin $providers $resource
    }

    $providerAuthentication = $null
    $callbackKey = $null
    $platformHost = Get-RunningAppHost $platform
    if ($platformHost) {
        foreach ($provider in $providerEndpoints.Keys) {
            $configured = Get-ResourceOrigin $platform "$provider-provider-api"
            if ($configured -ne $providerEndpoints[$provider]) {
                throw 'The running platform uses different provider URLs. Stop its AppHost and rerun tools/dev.ps1.'
            }
        }
        Write-Host 'Reusing the platform AppHost.'
        # The frontend mode is fixed when the platform starts; a preview build adds the frontend-build resource.
        $runningPreview = (Get-ResourceInstances $platform 'frontend-build').Count -gt 0
        if ($runningPreview -ne [bool]$Preview) {
            $requested = $Preview ? 'preview' : 'dev server'
            $running = $runningPreview ? 'preview' : 'dev server'
            Write-Warning "The running platform serves the frontend as $running, not $requested. Stop its AppHost and rerun tools/dev.ps1 to change it."
        }
    }
    else {
        $providerAuthentication = Get-WattHarborAuthentication
        # A running AppHost never reveals its generated secrets, so the launcher chooses the callback key itself.
        $callbackKey = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
        $platformParameters = @{
            'Parameters__wattharbor-provider-token-endpoint' = $providerAuthentication.TokenEndpoint
            'Parameters__wattharbor-provider-client-secret' = $providerAuthentication.ClientSecret
            'Parameters__wattharbor-callback-key' = $callbackKey
        }
        foreach ($provider in $providerEndpoints.Keys) {
            $platformParameters["Parameters__$provider-provider-url"] = $providerEndpoints[$provider]
        }
        # Set explicitly so a stale Frontend:Mode secret or inherited variable cannot change the default.
        $platformParameters['Frontend__Mode'] = $Preview ? 'preview' : 'dev'
        foreach ($name in $platformParameters.Keys) {
            $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, $platformParameters[$name], 'Process')
        }
        Start-AppHost $platform @('--launch-profile', 'http')
    }

    Invoke-Aspire -AppHost $platform -Arguments @(
        'wait', 'frontend', '--apphost', $platform, '--timeout', '300'
    ) | Out-Host
    Invoke-Aspire -AppHost $platform -Arguments @(
        'describe', 'frontend', '--apphost', $platform
    ) | Out-Host
    if ($callbackKey) {
        # The provider persists the target, so a reused platform keeps the registration of its own launch.
        Register-WattHarborCallback `
            -ProviderOrigin $providerEndpoints['wattharbor'] `
            -Authentication $providerAuthentication `
            -GatewayOrigin (Get-ResourceOrigin $platform 'gateway') `
            -CallbackKey $callbackKey
    }
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
