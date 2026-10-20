param([string]$ConfigPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/isolated/claude-mcp.json'),
    [ValidateSet('podman', 'docker')][string]$Runtime = 'podman')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$repoRoot = Split-Path $PSScriptRoot -Parent
$runId = [Guid]::NewGuid().ToString('N')
$previousRunId = $env:HSR_ACCEPTANCE_RUN_ID
try {
    $env:HSR_ACCEPTANCE_RUN_ID = $runId
    $output = & dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Mcp/bin/Release/net10.0/HeadlessScreenReader.Mcp.dll') --smoke-config $ConfigPath
    $code = $LASTEXITCODE
} finally { $env:HSR_ACCEPTANCE_RUN_ID = $previousRunId }
$result = ($output -join "`n") | ConvertFrom-Json
if ($code -ne 0 -or -not $result.PrototypePassed) {
    $result | ConvertTo-Json -Depth 5
    throw "PUBLIC_MCP_ACCEPTANCE_FAILED: $($result.Reason)"
}
# Windows may terminate the stdio launcher before its finally block. The worker heartbeat
# and proxy/fence expiry must still remove this run's containers without global cleanup.
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(25)
do {
    $current = (Invoke-ReaderRuntime $Runtime @('ps', '--all', '--filter', "label=hsr.prototype.acceptance=$runId", '--format', '{{.ID}}')).Text -split "`n"
    $remaining = @($current | Where-Object { $_ })
    if ($remaining.Count -eq 0) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $deadline)
$result | Add-Member -NotePropertyName ContainersReleased -NotePropertyValue ($remaining.Count -eq 0)
$result | ConvertTo-Json -Depth 5
if ($remaining.Count -ne 0) { throw 'PROTOTYPE_CONTAINERS_DID_NOT_EXPIRE' }
