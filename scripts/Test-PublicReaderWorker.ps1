param([ValidateSet('podman','docker')][string]$Runtime='podman')
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$repoRoot=Split-Path $PSScriptRoot -Parent
$lines=& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Cli/bin/Release/net10.0/HeadlessScreenReader.Cli.dll') --check-public-reader-worker $Runtime $repoRoot
$code=$LASTEXITCODE
$result=($lines -join "`n") | ConvertFrom-Json
if($result.AcceptanceRun -notmatch '^[0-9a-f]{32}$'){
    $result|ConvertTo-Json -Depth 5
    throw 'PUBLIC_READER_WORKER_PROOF_UNCONFIRMED'
}
$deadline=[DateTimeOffset]::UtcNow.AddSeconds(25)
do {
    $containers=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter',"label=hsr.prototype.acceptance=$($result.AcceptanceRun)",'--format','{{.ID}}')
    $networks=Invoke-ReaderRuntime $Runtime @('network','ls','--filter',"label=hsr.prototype.acceptance=$($result.AcceptanceRun)",'--format','{{.Name}}')
    if([string]::IsNullOrWhiteSpace($containers.Text) -and [string]::IsNullOrWhiteSpace($networks.Text)){break}
    Start-Sleep -Milliseconds 200
} while([DateTimeOffset]::UtcNow -lt $deadline)
$result|Add-Member -NotePropertyName ContainersReleased -NotePropertyValue ([string]::IsNullOrWhiteSpace($containers.Text))
$result|Add-Member -NotePropertyName NetworksReleased -NotePropertyValue ([string]::IsNullOrWhiteSpace($networks.Text))
$result|ConvertTo-Json -Depth 5
if($code -ne 0 -or -not $result.Ready){throw 'PUBLIC_READER_WORKER_PROOF_UNCONFIRMED'}
if(-not $result.ContainersReleased -or -not $result.NetworksReleased){throw 'PUBLIC_READER_OWNED_CLEANUP_UNCONFIRMED'}
