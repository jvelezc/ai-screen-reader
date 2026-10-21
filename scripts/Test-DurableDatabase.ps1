param([ValidateSet('podman', 'docker')][string]$Runtime = 'podman')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$runId = [Guid]::NewGuid().ToString('N')
$name = "hsr-database-$runId"
# Supabase PostgreSQL17.6.1.167, pgmq1.5.1 and pgTAP1.3.3. Never use latest.
$image = 'public.ecr.aws/supabase/postgres@sha256:16c0944c77885446d4f2ebbb2191b357feeb3b1295bde2fb1978c113129dbeb4'
$folder = Join-Path $repoRoot "artifacts/database/$runId"
New-Item -ItemType Directory -Path $folder | Out-Null
$created = $false; $removed = $false; $ready = $false; $stage = 'create'; $errorCode = $null
$cases = [Collections.Generic.List[string]]::new()
$script:discovered = 0; $script:executed = 0; $script:failed = 0
$oldPassword = $env:POSTGRES_PASSWORD

function Wait-DatabaseReady {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        # pg_isready also accepts the entrypoint's temporary initialization server.
        # PID1 executes postgres only after all image initialization scripts finish.
        # Nix truncates its executable comm name, so inspect argv0 rather than comm.
        $root = Invoke-ReaderRuntime $Runtime @('exec', $name, 'cat', '/proc/1/cmdline') -AllowFailure
        if ($root.Code -eq 0 -and $root.Text.Split([char]0)[0] -match '(^|/)postgres$') {
            $probe = Invoke-ReaderRuntime $Runtime @('exec', $name, 'pg_isready', '-U', 'postgres', '-d', 'postgres') -AllowFailure
            if ($probe.Code -eq 0) { return }
        }
        if ($clock.Elapsed.TotalSeconds -ge 60) { throw 'DATABASE_START_TIMEOUT' }
        Start-Sleep -Milliseconds 250
    } while ($true)
}

function Invoke-DatabaseFile {
    param([string]$RelativePath, [string]$LogName, [int]$Expected = 0, [string]$User = 'postgres', [switch]$Atomic)
    $null = Invoke-ReaderRuntime $Runtime @('cp', (Join-Path $repoRoot $RelativePath), "${name}:/tmp/hsr-test.sql")
    $sqlArgs = @('exec', '--env', 'PGOPTIONS=-c statement_timeout=15000', $name,
        'psql', '-X', '-U', $User, '-d', 'postgres', '-qAt', '-v', 'ON_ERROR_STOP=1')
    if ($Atomic) { $sqlArgs += '--single-transaction' }
    $result = Invoke-ReaderRuntime $Runtime ($sqlArgs + @('-f', '/tmp/hsr-test.sql')) -AllowFailure
    $result.Text | Set-Content (Join-Path $folder "$LogName.log") -Encoding utf8NoBOM
    if ($Expected -eq 0) {
        if ($result.Code -ne 0) { throw "DATABASE_SQL_FAILED_$LogName" }
        return
    }
    $plan = [regex]::Matches($result.Text, '(?m)^1\.\.([0-9]+)\r?$')
    $passed = [regex]::Matches($result.Text, '(?m)^ok ([0-9]+) - ([A-Za-z][A-Za-z0-9_]*)\r?$')
    if ($plan.Count -eq 1) { $script:discovered += [int]$plan[0].Groups[1].Value }
    $actualResults = [regex]::Matches($result.Text, '(?m)^(ok|not ok) ([0-9]+) - ')
    $script:executed += $actualResults.Count
    $script:failed += @($actualResults | Where-Object { $_.Groups[1].Value -eq 'not ok' }).Count
    foreach ($pass in $passed) {
        $caseName = $pass.Groups[2].Value
        if ($cases.Contains($caseName)) { throw 'DATABASE_DUPLICATE_CASE_NAME' }
        $cases.Add($caseName)
    }
    if ($result.Code -ne 0) { throw "DATABASE_SQL_FAILED_$LogName" }
    if ($plan.Count -ne 1 -or [int]$plan[0].Groups[1].Value -ne $Expected -or $passed.Count -ne $Expected `
        -or $result.Text -match '(?m)^not ok |# (SKIP|TODO)|^Bail out!') { throw "DATABASE_TAP_FAILED_$LogName" }
    for ($index = 0; $index -lt $Expected; $index++) {
        if ([int]$passed[$index].Groups[1].Value -ne $index + 1) { throw 'DATABASE_CASE_SEQUENCE_INVALID' }
    }
}

try {
    # Process-local random initialization password; no host service credentials.
    $env:POSTGRES_PASSWORD = [Guid]::NewGuid().ToString('N')
    $null = Invoke-ReaderRuntime $Runtime @('create', '--name', $name, '--network', 'none',
        '--memory', '768m', '--cpus', '2', '--pids-limit', '128',
        '--label', "hsr.database.owner=$runId", '--env', 'POSTGRES_PASSWORD', $image)
    $created = $true
    $env:POSTGRES_PASSWORD = $oldPassword
    $item = ((Invoke-ReaderRuntime $Runtime @('inspect', $name)).Text | ConvertFrom-Json)[0]
    if ($item.HostConfig.NetworkMode -ne 'none' -or $item.HostConfig.Privileged `
        -or @($item.Mounts | Where-Object { $_.Type -eq 'bind' }).Count -ne 0 `
        -or @($item.HostConfig.PortBindings.PSObject.Properties).Count -ne 0) { throw 'DATABASE_ISOLATION_UNCONFIRMED' }
    $stage = 'start'
    $null = Invoke-ReaderRuntime $Runtime @('start', $name)
    Wait-DatabaseReady
    $stage = 'migration'
    Invoke-DatabaseFile 'supabase/migrations/20261021194000_tenant_scan_queue.sql' 'migration' -Atomic
    $stage = 'authorization'
    Invoke-DatabaseFile 'tests/database/tenant-scan-queue.sql' 'authorization' -Expected 41
    $stage = 'persist'
    Invoke-DatabaseFile 'tests/database/before-restart.sql' 'persist' -Atomic
    $stage = 'restart'
    $null = Invoke-ReaderRuntime $Runtime @('restart', '--time', '10', $name)
    Wait-DatabaseReady
    Invoke-DatabaseFile 'tests/database/after-restart.sql' 'restart' -Expected 3
    $stage = 'concurrency'
    Invoke-DatabaseFile 'tests/database/concurrent-admission.sql' 'concurrency' -Expected 6 -User 'supabase_admin'
    $stage = 'completed'
    $ready = $true
} catch {
    $errorCode = "DATABASE_$($stage.ToUpperInvariant())_UNCONFIRMED"
    $_.Exception.Message | Set-Content (Join-Path $folder 'failure.log') -Encoding utf8NoBOM
} finally {
    $env:POSTGRES_PASSWORD = $oldPassword
    if ($created) {
        $read = Invoke-ReaderRuntime $Runtime @('inspect', $name) -AllowFailure
        if ($read.Code -eq 0) {
            $owned = ($read.Text | ConvertFrom-Json)[0]
            if ($owned.Config.Labels.'hsr.database.owner' -ne $runId) { $errorCode = 'DATABASE_OWNER_MISMATCH'; $ready = $false }
            else {
                $delete = Invoke-ReaderRuntime $Runtime @('rm', '--force', '--volumes', $name) -AllowFailure
                $remaining = Invoke-ReaderRuntime $Runtime @('inspect', $name) -AllowFailure
                $removed = $delete.Code -eq 0 -and $remaining.Code -ne 0
                if (-not $removed) { $errorCode = 'DATABASE_CLEANUP_UNCONFIRMED'; $ready = $false }
            }
        } else { $errorCode = 'DATABASE_RESOURCE_UNCONFIRMED'; $ready = $false }
    }
}
$receipt = [pscustomobject]@{
    Schema = 'hsr-database-admission-proof-v1'; Ready = $ready; Stage = $stage; ErrorCode = $errorCode
    Runtime = $Runtime; Image = $image; Expected = 50; Discovered = $script:discovered
    Executed = $script:executed; Passed = $cases.Count; Failed = $script:failed
    Unconfirmed = [Math]::Max(0, 50 - $script:executed); Skipped = 0; Cases = @($cases)
    ContainerRemoved = $removed; HostedProjectChanged = $false; WorkerExecutionPerformed = $false
    Folder = $folder
}
$receipt | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $folder 'run.json') -Encoding utf8NoBOM
$receipt | ConvertTo-Json -Depth 4
if (-not $ready -or -not $removed -or $cases.Count -ne 50 -or $script:discovered -ne 50 `
    -or $script:executed -ne 50 -or $script:failed -ne 0) { exit 1 }
