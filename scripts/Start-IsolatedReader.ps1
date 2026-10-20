param([ValidateSet('podman', 'docker')][string]$Runtime = 'podman', [string]$AllowedHosts = 'example.com,iana.org,www.iana.org')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$ctx = Start-ReaderIsolation -Runtime $Runtime -AllowedHosts $AllowedHosts
try {
    $arguments = (Get-ReaderWorkerArguments $ctx) + @('--interactive', $ctx.Image, '--isolated', $ctx.ProxyIp, $AllowedHosts, $ctx.Owner)
    # Preserve the MCP byte streams. Infrastructure commands above are captured; only the worker owns stdout.
    & $Runtime @arguments
    if ($LASTEXITCODE -ne 0) { throw 'ISOLATED_READER_EXITED_WITH_ERROR' }
} finally { Stop-ReaderIsolation $ctx }
