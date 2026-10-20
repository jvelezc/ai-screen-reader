param([ValidateSet('podman', 'docker')][string]$Runtime = 'podman', [string]$AllowedHosts = 'example.com,www.iana.org')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$ctx = Start-ReaderIsolation -Runtime $Runtime -AllowedHosts $AllowedHosts
try {
    $arguments = (Get-ReaderWorkerArguments $ctx) + @('--entrypoint', 'dotnet', $ctx.Image,
        '/app/egress/HeadlessScreenReader.Egress.dll', 'probe', $ctx.ProxyIp, $AllowedHosts)
    $run = Invoke-ReaderRuntime $Runtime $arguments -AllowFailure
    $probe = $run.Text | ConvertFrom-Json
    [pscustomobject]@{ Control = $ctx.Control; Probe = $probe; DefaultDropPolicies = $ctx.KernelPolicies.Count; Runtime = $Runtime } | ConvertTo-Json -Depth 5
    if ($run.Code -ne 0 -or -not $probe.Passed) { throw 'ISOLATION_PROBE_FAILED; see the emitted receipt for each check.' }
} finally { Stop-ReaderIsolation $ctx }
