#Requires -Version 7.0
<#
.SYNOPSIS
    Development helper: drive the transfer + HITL flow against a locally running API.

.EXAMPLE
    ./scripts/try-transfer.ps1 invoke "Transfer 12500 EUR to DE89 3704 0044 0532 0130 00"
    ./scripts/try-transfer.ps1 get    <sessionId>
    ./scripts/try-transfer.ps1 resume <sessionId> [true|false]

.NOTES
    BASE_URL defaults to the "fake" launch profile (http://localhost:5002); set
    $env:BASE_URL = 'http://localhost:5270' for the "http" profile (SQL Server + Ollama).
    API_KEY defaults to the first key for TENANT_ID in appsettings.Development.json (gitignored).
#>
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('invoke', 'get', 'resume')]
    [string] $Command,

    [Parameter(Mandatory, Position = 1)]
    [string] $Value,

    [Parameter(Position = 2)]
    [string] $Extra
)

$ErrorActionPreference = 'Stop'

$baseUrl = $env:BASE_URL ?? 'http://localhost:5002'
$tenantId = $env:TENANT_ID ?? 'tenant-local'
$apiKey = $env:API_KEY

if (-not $apiKey) {
    $settings = Join-Path $PSScriptRoot '..' 'src' 'AgenticWorkflowPoC.Api' 'appsettings.Development.json'
    if (Test-Path $settings) {
        $keys = (Get-Content $settings -Raw | ConvertFrom-Json).Authentication.ApiKeys
        $apiKey = ($keys | Where-Object TenantId -eq $tenantId | Select-Object -First 1).Key
    }
}

if (-not $apiKey) {
    Write-Error 'No API key: set $env:API_KEY or add Authentication:ApiKeys to appsettings.Development.json.'
}

$sessions = "$baseUrl/api/v1/tenants/$tenantId/sessions"

function Invoke-Api([string] $Method, [string] $Uri, [hashtable] $Body) {
    $request = @{
        Method             = $Method
        Uri                = $Uri
        Headers            = @{ 'X-Api-Key' = $apiKey }
        SkipHttpErrorCheck = $true
    }
    if ($Body) {
        $request.Body = $Body | ConvertTo-Json -Compress
        $request.ContentType = 'application/json'
    }

    $response = Invoke-WebRequest @request
    "HTTP $($response.StatusCode)"
    if ($response.Headers['Location']) {
        "Location: $($response.Headers['Location'] -join ', ')"
    }
    $response.Content
}

switch ($Command) {
    'invoke' {
        $sessionId = if ($Extra) { $Extra } else { [guid]::NewGuid().ToString() }
        Invoke-Api POST "$baseUrl/api/agent/invoke" @{ sessionId = $sessionId; prompt = $Value }
    }
    'get' {
        Invoke-Api GET "$sessions/$Value" $null
    }
    'resume' {
        Invoke-Api POST "$sessions/$Value/resume" @{ isApproved = ($Extra -ne 'false') }
    }
}
