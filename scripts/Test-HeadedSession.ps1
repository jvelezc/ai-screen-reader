param([ValidateSet('podman', 'docker')][string]$Runtime = 'podman', [switch]$SkipBuild,
    [ValidateSet('controller', 'synthetic-handoff', 'private-pixels', 'private-client')][string]$Scenario = 'controller')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$runId = [Guid]::NewGuid().ToString('N')
$name = "hsr-headed-$runId"
$image = "localhost/hsr-headed-proof:$runId"
$folder = Join-Path $repoRoot "artifacts/headed-linux/$runId"
New-Item -ItemType Directory -Path $folder | Out-Null
$stage = 'publish'; $errorCode = $null; $copied = $false; $created = $false; $removed = $false; $ready = $false
$built = $false; $imageRemoved = $false
$constraints = $null; $proofStage = $null; $proofErrorCode = $null
$taskLimit = if ($Scenario -eq 'private-client') { 512 } else { 256 }
try {
    if (-not $SkipBuild) {
        dotnet publish (Join-Path $repoRoot 'src/HeadlessScreenReader.Cli/HeadlessScreenReader.Cli.csproj') -c Release `
            -p:RestoreLockedMode=true -p:PlaywrightPlatform=linux-x64 -o (Join-Path $repoRoot 'artifacts/headed-build/cli')
        if ($LASTEXITCODE -ne 0) { throw 'HEADED_PUBLISH_FAILED' }
    }
    $stage = 'image'
    $build = Invoke-ReaderRuntime $Runtime @('build', '--tag', $image, '--label', "hsr.headed.owner=$runId", '--file', (Join-Path $repoRoot 'ops/headed/Containerfile'), $repoRoot) -AllowFailure
    $build.Text | Set-Content (Join-Path $folder 'build.log') -Encoding utf8NoBOM
    if ($build.Code -ne 0) { throw 'HEADED_IMAGE_BUILD_FAILED' }
    $built = $true
    $stage = 'create'
    $security = @()
    if ($Runtime -eq 'docker') { $security = @('--security-opt', "seccomp=$(Join-Path $repoRoot 'ops/prototype/playwright-seccomp.json')") }
    $arguments = @('create', '--name', $name, '--network', 'none', '--user', '1001:1001', '--cap-drop', 'ALL', '--cap-add', 'SYS_CHROOT',
        '--security-opt', 'no-new-privileges', '--memory', '1024m', '--cpus', '2', '--pids-limit', "$taskLimit", '--shm-size', '256m',
        '--label', "hsr.headed.owner=$runId", '--env', "HSR_HEADED_CASE=$Scenario") + $security + @($image)
    $null = Invoke-ReaderRuntime $Runtime $arguments
    $created = $true
    $stage = 'run'
    $null = Invoke-ReaderRuntime $Runtime @('start', $name)
    $runClock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $item = ((Invoke-ReaderRuntime $Runtime @('inspect', $name)).Text | ConvertFrom-Json)[0]
        if (-not $item.State.Running) { break }
        if ($runClock.Elapsed.TotalSeconds -ge 120) { throw 'HEADED_RUN_TIMEOUT' }
        Start-Sleep -Milliseconds 500
    } while ($true)
    $constraints = [pscustomobject]@{ NetworkMode = $item.HostConfig.NetworkMode; User = $item.Config.User;
        Privileged = $item.HostConfig.Privileged; CapAdd = @($item.HostConfig.CapAdd); CapDrop = @($item.HostConfig.CapDrop);
        SecurityOptions = @($item.HostConfig.SecurityOpt); MemoryBytes = $item.HostConfig.Memory; PidsLimit = $item.HostConfig.PidsLimit;
        ReadOnlyRoot = $item.HostConfig.ReadonlyRootfs; HostBindMounts = @($item.Mounts | Where-Object { $_.Type -eq 'bind' }).Count }
    $stage = 'copy'
    $null = Invoke-ReaderRuntime $Runtime @('cp', "${name}:/tmp/hsr-headed-proof/.", $folder)
    $copied = $true
    $stage = 'assess'
    $receipt = Get-Content (Join-Path $folder 'run.json') -Raw | ConvertFrom-Json
    $cleanup = Get-Content (Join-Path $folder 'cleanup.json') -Raw | ConvertFrom-Json
    if ($receipt.Stage -match '^[a-z-]{1,32}$') { $proofStage = $receipt.Stage }
    if ($receipt.ErrorCode -match '^[A-Z_]{1,80}$') { $proofErrorCode = $receipt.ErrorCode }
    $limitsObserved = $constraints.NetworkMode -eq 'none' -and $constraints.User -eq '1001:1001' -and -not $constraints.Privileged `
        -and $receipt.Kernel.Restricted -eq $true -and @($constraints.SecurityOptions | Where-Object { $_ -like 'no-new-privileges*' }).Count -eq 1 `
        -and $constraints.MemoryBytes -gt 0 -and $constraints.MemoryBytes -le 1073741824 `
        -and $constraints.PidsLimit -eq $taskLimit -and $constraints.HostBindMounts -eq 0
    $expectedSchema = if ($Scenario -eq 'private-client') { 'hsr-private-view-client-v1' }
        elseif ($Scenario -eq 'private-pixels') { 'hsr-private-pixel-proof-v1' }
        elseif ($Scenario -eq 'synthetic-handoff') { 'hsr-synthetic-handoff-proof-v1' } else { 'hsr-headed-session-proof-v1' }
    $ready = $item.State.ExitCode -eq 0 -and $limitsObserved -and $receipt.Schema -eq $expectedSchema `
        -and $receipt.Ready -eq $true -and $cleanup.displayCleanupConfirmed -eq $true `
        -and $cleanup.commandExitCode -eq 0 -and $receipt.BrowserCloseConfirmed -eq $true
    if (-not $ready) { throw 'HEADED_ACCEPTANCE_FAILED' }
    $stage = 'completed'
} catch {
    $errorCode = "HEADED_$($stage.ToUpperInvariant())_UNCONFIRMED"
} finally {
    if ($created) {
        $inspect = Invoke-ReaderRuntime $Runtime @('inspect', $name) -AllowFailure
        if ($inspect.Code -eq 0) {
            $owned = ($inspect.Text | ConvertFrom-Json)[0]
            if ($owned.Config.Labels.'hsr.headed.owner' -ne $runId) { $errorCode = 'HEADED_RESOURCE_OWNER_MISMATCH'; $ready = $false }
            else {
                if ($owned.State.Running) { $null = Invoke-ReaderRuntime $Runtime @('stop', '--time', '5', $name) -AllowFailure }
                if (-not $copied) {
                    $copy = Invoke-ReaderRuntime $Runtime @('cp', "${name}:/tmp/hsr-headed-proof/.", $folder) -AllowFailure
                    $copied = $copy.Code -eq 0
                }
                if ($copied) {
                    $remove = Invoke-ReaderRuntime $Runtime @('rm', $name) -AllowFailure
                    $removed = $remove.Code -eq 0
                }
                if (-not $removed) { $ready = $false; $errorCode ??= 'HEADED_CLEANUP_UNCONFIRMED' }
            }
        } else { $ready = $false; $errorCode ??= 'HEADED_CLEANUP_UNCONFIRMED' }
    }
    if ($built -and (-not $created -or $removed)) {
        $readImage = Invoke-ReaderRuntime $Runtime @('image', 'inspect', $image) -AllowFailure
        if ($readImage.Code -eq 0 -and (($readImage.Text | ConvertFrom-Json)[0]).Config.Labels.'hsr.headed.owner' -eq $runId) {
            $removeImage = Invoke-ReaderRuntime $Runtime @('image', 'rm', $image) -AllowFailure
            $imageRemoved = $removeImage.Code -eq 0
        }
        if (-not $imageRemoved) { $ready = $false; $errorCode ??= 'HEADED_IMAGE_CLEANUP_UNCONFIRMED' }
    }
}
$result = [pscustomobject]@{ Schema = 'hsr-headed-runtime-v1'; RunId = $runId; Runtime = $Runtime; Ready = $ready;
    Scenario = $Scenario;
    CaseName = $(if ($Scenario -eq 'private-client') { 'PrivateClient_ReadOnlyPixelsPauseExclusionRevocationAndOwnedCleanup' }
        elseif ($Scenario -eq 'private-pixels') { 'PrivatePixels_ActualReadOnlyRelayExcludesSyntheticGapAndRevokesOwnedDelivery' }
        elseif ($Scenario -eq 'synthetic-handoff') { 'SyntheticHandoff_ActualInputIsExcludedFromOwnedFilesAndOriginalMedia' }
        else { 'HeadedChromium_TwoControllerGenerationsPreserveProcessContextAndCleanOwnedDisplay' });
    Stage = $stage; ErrorCode = $errorCode; EvidenceCopied = $copied; ContainerRemoved = $removed; ImageRemoved = $imageRemoved;
    ProofStage = $proofStage; ProofErrorCode = $proofErrorCode; ObservedConstraints = $constraints;
    RetainedImage = $(if ($built -and -not $imageRemoved) { $image } else { $null });
    RetainedContainer = $(if ($created -and -not $removed) { $name } else { $null }); Folder = $folder }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'runtime.json') -Encoding utf8NoBOM
$result | ConvertTo-Json
if (-not $ready) { exit 3 }
