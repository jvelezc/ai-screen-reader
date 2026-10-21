param([ValidateSet('podman','docker')][string]$Runtime='podman')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$owner=[Guid]::NewGuid().ToString('N'); $name="hsr-worker-db-$owner"; $image="localhost/hsr-worker-proof:$owner"
$folder=Join-Path $repoRoot "artifacts/worker-linux/$owner"
New-Item -ItemType Directory -Path $folder | Out-Null
$postgres='public.ecr.aws/supabase/postgres@sha256:16c0944c77885446d4f2ebbb2191b357feeb3b1295bde2fb1978c113129dbeb4'
$created=$false; $built=$false; $removed=$false; $imageRemoved=$false; $ready=$false; $stage='publish'; $errorCode=$null
$cases=[Collections.Generic.List[string]]::new()
$script:executed=0; $script:failed=0
$expected=if($IsLinux){17}else{15}; $linuxSignalPerformed=$false
$recoveryResources=[Collections.Generic.List[string]]::new()
$names=@('POSTGRES_PASSWORD','HSR_WORKER_DATABASE','HSR_WORKER_IMAGE','HSR_WORKER_EVIDENCE','HSR_WORKER_RUNTIME','HSR_WORKER_SECCOMP')
$prior=@{}; foreach($key in $names){$prior[$key]=[Environment]::GetEnvironmentVariable($key)}
function Assert-Proof([bool]$condition,[string]$case){$script:executed++;if(-not $condition){$script:failed++;throw "WORKER_CASE_UNCONFIRMED_$case"};$cases.Add($case)}
function Invoke-ProofSql([string]$query){
    # SQL (including disposable test password) goes through stdin, never command arguments or retained files.
    $text=@($query | & $Runtime exec --interactive $name psql -X -U postgres -d postgres -qAt -v ON_ERROR_STOP=1 2>&1)
    if($LASTEXITCODE -ne 0){throw 'WORKER_DATABASE_SQL_UNCONFIRMED'}
    return ($text -join "`n")
}
try {
    dotnet publish (Join-Path $repoRoot 'src/HeadlessScreenReader.Cli/HeadlessScreenReader.Cli.csproj') -c Release -p:RestoreLockedMode=true -p:PlaywrightPlatform=linux-x64 -o (Join-Path $repoRoot 'artifacts/worker-build/cli') | Out-Null
    if($LASTEXITCODE -ne 0){throw 'WORKER_FIXTURE_PUBLISH_FAILED'}
    dotnet build (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/HeadlessScreenReader.Worker.csproj') -c Release -p:RestoreLockedMode=true | Out-Null
    if($LASTEXITCODE -ne 0){throw 'WORKER_BUILD_FAILED'}
    $stage='image'
    $build=Invoke-ReaderRuntime $Runtime @('build','--tag',$image,'--label',"hsr.worker.proof=$owner",'--file',(Join-Path $repoRoot 'ops/worker/Containerfile'),$repoRoot) -AllowFailure
    $build.Text | Set-Content (Join-Path $folder 'build.log') -Encoding utf8NoBOM
    if($build.Code -ne 0){throw 'WORKER_IMAGE_BUILD_FAILED'}; $built=$true
    $imageId=(Invoke-ReaderRuntime $Runtime @('image','inspect','--format','{{.Id}}',$image)).Text.Trim()
    if($imageId -match '^[a-f0-9]{64}$'){$imageId='sha256:'+$imageId}
    if($imageId -notmatch '^sha256:[a-f0-9]{64}$'){throw 'WORKER_IMAGE_ID_UNCONFIRMED'}
    $stage='database'
    $postgres=Resolve-ReaderDatabaseImage $Runtime
    $env:POSTGRES_PASSWORD=[Guid]::NewGuid().ToString('N')
    $null=Invoke-ReaderRuntime $Runtime @('create','--name',$name,'--publish','127.0.0.1::5432','--memory','768m','--cpus','2','--pids-limit','128','--label',"hsr.worker.proof=$owner",'--env','POSTGRES_PASSWORD',$postgres)
    $created=$true; $env:POSTGRES_PASSWORD=$prior['POSTGRES_PASSWORD']
    $null=Invoke-ReaderRuntime $Runtime @('start',$name)
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do {
        $root=Invoke-ReaderRuntime $Runtime @('exec',$name,'cat','/proc/1/cmdline') -AllowFailure
        $probe=Invoke-ReaderRuntime $Runtime @('exec',$name,'pg_isready','-U','postgres','-d','postgres') -AllowFailure
        if($root.Code -eq 0 -and $root.Text.Split([char]0)[0] -match '(^|/)postgres$' -and $probe.Code -eq 0){break}
        if($clock.Elapsed.TotalSeconds -gt 60){throw 'WORKER_DATABASE_START_TIMEOUT'}
        Start-Sleep -Milliseconds 250
    } while($true)
    $stage='migration'
    foreach($migration in Get-ChildItem (Join-Path $repoRoot 'supabase/migrations') -Filter '*.sql' | Sort-Object Name){
        $null=Invoke-ReaderRuntime $Runtime @('cp',$migration.FullName,"${name}:/tmp/migration.sql")
        $null=Invoke-ReaderRuntime $Runtime @('exec',$name,'psql','-X','-U','postgres','-d','postgres','-qAt','-v','ON_ERROR_STOP=1','--single-transaction','-f','/tmp/migration.sql')
    }
    $user=[Guid]::NewGuid(); $org=[Guid]::NewGuid(); $request=[Guid]::NewGuid(); $password=[Guid]::NewGuid().ToString('N')
    $null=Invoke-ProofSql "create role hsr_proof_worker login password '$password'; grant hsr_worker to hsr_proof_worker; insert into auth.users(id) values('$user'); insert into hsr.organizations(id,name) values('$org','Worker acceptance'); insert into hsr.memberships values('$org','$user');"
    $admission=Invoke-ProofSql "begin; set local role authenticated; select set_config('request.jwt.claim.sub','$user',true); select row_to_json(r) from hsr.create_scan('$org','$request') r; commit;"
    $job=($admission -split "`n" | Where-Object {$_ -match '^\{'}) | ConvertFrom-Json
    if(-not $job.job_id){throw 'WORKER_ADMISSION_UNCONFIRMED'}
    $stage='execute'
    $port=(Invoke-ReaderRuntime $Runtime @('port',$name,'5432/tcp')).Text.Trim()
    if($port -notmatch '^127\.0\.0\.1:([0-9]+)$'){throw 'WORKER_DATABASE_LOOPBACK_UNCONFIRMED'}
    $env:HSR_WORKER_DATABASE="Host=127.0.0.1;Port=$($Matches[1]);Username=hsr_proof_worker;Password=$password;Database=postgres;SSL Mode=Disable"
    $env:HSR_WORKER_IMAGE=$imageId; $env:HSR_WORKER_RUNTIME=$Runtime; $env:HSR_WORKER_EVIDENCE=Join-Path $folder 'evidence'
    $env:HSR_WORKER_SECCOMP=Join-Path $repoRoot 'ops/prototype/playwright-seccomp.json'
    $output=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --once 2>&1)
    $workerExit=$LASTEXITCODE
    $output | Set-Content (Join-Path $folder 'worker.log') -Encoding utf8NoBOM
    $result=($output -join "`n") | ConvertFrom-Json
    Assert-Proof ($workerExit -eq 0 -and $result.State -eq 'Observed' -and $result.JobId -eq $job.job_id) 'RealWorkerCompletesOnlyTheAdmittedJob'
    $path=Join-Path $env:HSR_WORKER_EVIDENCE $result.Reference
    $bytes=[IO.File]::ReadAllBytes($path); $observation=[Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    Assert-Proof ($observation.Job.OrganizationId -eq "$org" -and $observation.Job.ScanId -eq $job.scan_id -and $observation.Job.JobId -eq $job.job_id -and $observation.Job.Generation -eq 1) 'PersistedObservationBindsActualTenantScanJobAndAttempt'
    Assert-Proof (@($observation.Snapshot.Nodes | Where-Object {$_.Role -eq 'heading' -and $_.Name -eq 'Synthetic shop'}).Count -eq 1 -and $observation.DispatchedInputs -eq 0 -and -not $observation.GoalVerified) 'ActualChromiumObservationDoesNotInventInputOrGoalVerification'
    Assert-Proof ($observation.Shutdown.ProcessExitConfirmed -and $observation.Shutdown.ActiveCalls -eq 0 -and $observation.Shutdown.ProcessExitConfirmationMilliseconds -lt 4000 -and $observation.Shutdown.NativeElapsedMilliseconds -lt 5000) 'ActualBrowserExitAndNativeDrainMeetOriginalDeadlines'
    Assert-Proof ([Text.Encoding]::UTF8.GetString($bytes) -notmatch 'traversal-(account|secret)-canary|lease_token|sbp_|service_role') 'StoredFixtureObservationExcludesEditableCanariesAndCapabilities'
    $facts=(Invoke-ProofSql "select json_build_object('state',s.state,'digest',j.result_digest,'reference',j.result_reference,'queue',(select count(*) from pgmq.q_hsr_scan_jobs),'leases',(select count(*) from hsr.job_leases),'attempts',(select count(*) from hsr.job_attempts),'renewed',(select count(*) from hsr.attempt_events where kind='Renewed'),'completed',(select count(*) from hsr.attempt_events where kind='Completed')) from hsr.scans s join hsr.scan_jobs j on j.organization_id=s.organization_id and j.scan_id=s.id where j.id='$($job.job_id)'" ) | ConvertFrom-Json
    Assert-Proof ($facts.state -eq 'observed' -and $facts.digest -eq (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -and $facts.reference -eq $result.Reference) 'DatabaseCompletionAcknowledgesExactPersistedBytes'
    Assert-Proof ($facts.queue -eq 0 -and $facts.leases -eq 0 -and $facts.attempts -eq 1 -and $facts.completed -eq 1) 'ActualQueueAckRemovesCapabilityAndRetainsOneImmutableAttempt'
    Assert-Proof ($facts.renewed -ge 1) 'ActualNativeAdapterRenewsCurrentServerCapabilityBeforeExecution'
    $remaining=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter','name=^hsr-observation-','--format','{{.ID}}')
    Assert-Proof ([string]::IsNullOrWhiteSpace($remaining.Text)) 'OwnedObservationContainerIsRemovedBeforeCompletion'
    $stage='blocked-execution'
    $failureRequest=[Guid]::NewGuid()
    $admission=Invoke-ProofSql "begin; set local role authenticated; select set_config('request.jwt.claim.sub','$user',true); select row_to_json(r) from hsr.create_scan('$org','$failureRequest') r; commit;"
    $failedJob=($admission -split "`n" | Where-Object {$_ -match '^\{'}) | ConvertFrom-Json
    $env:HSR_WORKER_IMAGE='sha256:'+('f'*64)
    $output=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --once 2>&1)
    $workerExit=$LASTEXITCODE; $result=($output -join "`n") | ConvertFrom-Json
    Assert-Proof ($workerExit -eq 3 -and $result.State -eq 'Blocked' -and $result.ErrorCode -eq 'FIXTURE_OBSERVATION_UNCONFIRMED' -and $result.JobId -eq $failedJob.job_id) 'MissingImmutableImageProducesSafeBlockedEngineOutcome'
    $failure=(Invoke-ProofSql "select json_build_object('state',j.state,'code',j.last_error_code,'digest',j.result_digest,'queue',(select count(*) from pgmq.q_hsr_scan_jobs),'leases',(select count(*) from hsr.job_leases),'archived',(select count(*) from pgmq.a_hsr_scan_jobs),'blocked',(select count(*) from hsr.attempt_events where job_id=j.id and kind='Blocked')) from hsr.scan_jobs j where j.id='$($failedJob.job_id)'" ) | ConvertFrom-Json
    Assert-Proof ($failure.state -eq 'blocked' -and $failure.code -eq 'FIXTURE_OBSERVATION_UNCONFIRMED' -and $null -eq $failure.digest -and $failure.queue -eq 0 -and $failure.leases -eq 0 -and $failure.archived -eq 1 -and $failure.blocked -eq 1) 'ActualNativeBlockArchivesMessageWithoutInventingObservation'
    if($IsLinux){
        $stage='linux-stop'
        $start=[Diagnostics.ProcessStartInfo]::new('dotnet')
        $start.ArgumentList.Add((Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll'))
        $start.ArgumentList.Add('--poll'); $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true; $start.UseShellExecute=$false
        $process=[Diagnostics.Process]::new(); $process.StartInfo=$start
        try {
            if(-not $process.Start()){throw 'WORKER_POLL_START_UNCONFIRMED'}
            $line=$process.StandardOutput.ReadLineAsync()
            if(-not $line.Wait(10000) -or ($line.Result|ConvertFrom-Json).State -ne 'Empty'){throw 'WORKER_POLL_READY_UNCONFIRMED'}
            $second=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --recover 2>&1)
            $secondExit=$LASTEXITCODE
            Assert-Proof ($secondExit -eq 3 -and -not $process.HasExited) 'ConcurrentNativeCoordinatorCannotAcquireRetainedOwnership'
            & /bin/kill -TERM -- $process.Id
            if($LASTEXITCODE -ne 0){throw 'WORKER_SIGNAL_UNCONFIRMED'}
            $exited=$process.WaitForExit(5000)
            Assert-Proof ($exited -and $process.ExitCode -eq 0) 'ActualLinuxWorkerHonorsSigtermWithoutAutomaticRestart'
            $linuxSignalPerformed=$true
        } finally {
            if(-not $process.HasExited){$process.Kill();$null=$process.WaitForExit(5000)}
            $process.Dispose()
        }
    }
    $stage='native-recovery'
    $env:HSR_WORKER_IMAGE=$imageId
    $env:HSR_WORKER_EVIDENCE=Join-Path $folder 'recovery-evidence'
    $intentRoot=Join-Path $env:HSR_WORKER_EVIDENCE '.runtime/intents'
    New-Item -ItemType Directory -Force $intentRoot | Out-Null
    $nativeOwner=[Guid]::NewGuid(); $target="hsr-observation-$($nativeOwner.ToString('N'))"; $sentinel="hsr-worker-sentinel-$owner"
    $descriptor=@{OrganizationId="$org";ScanId="$([Guid]::NewGuid())";JobId="$([Guid]::NewGuid())";Generation=1}
    $intent=@{Schema='hsr-native-ownership-v1';Owner="$nativeOwner";Job=$descriptor;Runtime=$Runtime;Image=$imageId}
    $intentPath=Join-Path $intentRoot "$($nativeOwner.ToString('N')).json"
    $intent|ConvertTo-Json -Depth 4|Set-Content $intentPath -Encoding utf8NoBOM
    $targetLabels=@('--label',"hsr.observation.owner=$($nativeOwner.ToString('N'))",'--label',"hsr.observation.organization=$($org.ToString('N'))",'--label',"hsr.observation.scan=$($descriptor.ScanId.Replace('-',''))",'--label',"hsr.observation.job=$($descriptor.JobId.Replace('-',''))",'--label','hsr.observation.generation=1')
    foreach($resource in @($target,$sentinel)){
        $labels=if($resource -eq $target){$targetLabels}else{@()}
        $null=Invoke-ReaderRuntime $Runtime (@('create','--name',$resource,'--network','none','--user','1001:1001','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges','--label',"hsr.worker.proof=$owner",'--entrypoint','/bin/sh')+$labels+@($imageId,'-c','sleep 120'))
        $recoveryResources.Add($resource);$null=Invoke-ReaderRuntime $Runtime @('start',$resource)
    }
    $database=$env:HSR_WORKER_DATABASE;$env:HSR_WORKER_DATABASE=$null
    try {
        $output=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --recover 2>&1)
        $recoveryExit=$LASTEXITCODE;$result=($output -join "`n")|ConvertFrom-Json
    } finally {$env:HSR_WORKER_DATABASE=$database}
    $targetGone=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter',"name=^$target`$",'--format','{{.ID}}')
    $unrelated=((Invoke-ReaderRuntime $Runtime @('inspect',$sentinel)).Text|ConvertFrom-Json)[0]
    Assert-Proof ($recoveryExit -eq 0 -and $result.State -eq 'Recovered' -and $result.Recovered -eq 1 -and [string]::IsNullOrWhiteSpace($targetGone.Text) -and $unrelated.State.Running) 'RetainedNativeIntentRecoveryRemovesOnlyItsBoundContainer'
    $removal=Get-Content (Join-Path $env:HSR_WORKER_EVIDENCE ".runtime/removals/$($nativeOwner.ToString('N')).json") -Raw|ConvertFrom-Json
    Assert-Proof (-not (Test-Path $intentPath) -and $removal.ContainerAbsenceConfirmed -and $removal.RecoveredAfterRestart -and $removal.Intent.Job.JobId -eq $descriptor.JobId) 'RecoveryPersistsActualAbsenceBeforeRetiringIntent'
    $unchanged=(Invoke-ProofSql "select json_build_object('jobs',(select count(*) from hsr.scan_jobs),'attempts',(select count(*) from hsr.job_attempts),'completed',(select count(*) from hsr.attempt_events where kind='Completed'),'queue',(select count(*) from pgmq.q_hsr_scan_jobs))")|ConvertFrom-Json
    Assert-Proof ($unchanged.jobs -eq 2 -and $unchanged.attempts -eq 2 -and $unchanged.completed -eq 1 -and $unchanged.queue -eq 0 -and @(Get-ChildItem $env:HSR_WORKER_EVIDENCE -Recurse -Filter observation.json).Count -eq 0) 'NativeRecoveryNeedsNoDatabaseCredentialAndCannotInventObservation'
    # A retained intent never authorizes deleting a same-name container with another owner.
    $badOwner=[Guid]::NewGuid();$badTarget="hsr-observation-$($badOwner.ToString('N'))"
    $intent.Owner="$badOwner"; $badPath=Join-Path $intentRoot "$($badOwner.ToString('N')).json"
    $intent|ConvertTo-Json -Depth 4|Set-Content $badPath -Encoding utf8NoBOM
    $null=Invoke-ReaderRuntime $Runtime @('create','--name',$badTarget,'--network','none','--user','1001:1001','--label',"hsr.worker.proof=$owner",'--label',"hsr.observation.owner=$([Guid]::NewGuid().ToString('N'))",'--entrypoint','/bin/sh',$imageId,'-c','sleep 120')
    $recoveryResources.Add($badTarget);$null=Invoke-ReaderRuntime $Runtime @('start',$badTarget)
    $output=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --recover 2>&1)
    $failedRecoveryExit=$LASTEXITCODE
    $unowned=((Invoke-ReaderRuntime $Runtime @('inspect',$badTarget)).Text|ConvertFrom-Json)[0]
    Assert-Proof ($failedRecoveryExit -eq 3 -and $unowned.State.Running -and (Test-Path $badPath)) 'MismatchedNativeOwnerFailsClosedAndRetainsIntent'
    $stage='completed'; $ready=$true
} catch {
    $errorCode="WORKER_$($stage.ToUpperInvariant())_UNCONFIRMED"
    $_.Exception.Message | Set-Content (Join-Path $folder 'failure.log') -Encoding utf8NoBOM
} finally {
    foreach($key in $names){[Environment]::SetEnvironmentVariable($key,$prior[$key])}
    foreach($resource in $recoveryResources){
        $probe=Invoke-ReaderRuntime $Runtime @('inspect',$resource) -AllowFailure
        if($probe.Code -eq 0){
            $item=($probe.Text|ConvertFrom-Json)[0]
            if($item.Config.Labels.'hsr.worker.proof' -ne $owner){throw 'WORKER_PROOF_CLEANUP_OWNER_MISMATCH'}
            $null=Invoke-ReaderRuntime $Runtime @('rm','--force','--volumes',$resource)
        }
    }
    if($created){
        $item=((Invoke-ReaderRuntime $Runtime @('inspect',$name)).Text | ConvertFrom-Json)[0]
        if($item.Config.Labels.'hsr.worker.proof' -eq $owner){
            $delete=Invoke-ReaderRuntime $Runtime @('rm','--force','--volumes',$name) -AllowFailure
            $remaining=Invoke-ReaderRuntime $Runtime @('inspect',$name) -AllowFailure
            $removed=$delete.Code -eq 0 -and $remaining.Code -ne 0
        }
    }
    if($built){
        $item=((Invoke-ReaderRuntime $Runtime @('image','inspect',$image)).Text | ConvertFrom-Json)[0]
        if($item.Config.Labels.'hsr.worker.proof' -eq $owner){$delete=Invoke-ReaderRuntime $Runtime @('image','rm',$image) -AllowFailure; $imageRemoved=$delete.Code -eq 0}
    }
}
$receipt=@{Schema='hsr-observation-worker-proof-v1';Ready=$ready;Stage=$stage;ErrorCode=$errorCode;Runtime=$Runtime;Expected=$expected;Discovered=$expected;Executed=$script:executed;Passed=$cases.Count;Failed=$script:failed;Unconfirmed=$expected-$script:executed;Skipped=0;Cases=@($cases);DatabaseRemoved=$removed;ImageRemoved=$imageRemoved;LinuxSignalPerformed=$linuxSignalPerformed;HostedProjectChanged=$false;Folder=$folder}
$receipt | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $folder 'run.json') -Encoding utf8NoBOM
$receipt | ConvertTo-Json -Depth 4
if(-not $ready -or -not $removed -or -not $imageRemoved -or $cases.Count -ne $expected){exit 1}
