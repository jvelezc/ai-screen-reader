param([ValidateSet('podman','docker')][string]$Runtime='podman', [switch]$PublishRelease)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$sourceCommit=(& git -C $repoRoot rev-parse HEAD).Trim()
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$owner=[Guid]::NewGuid().ToString('N'); $name="hsr-worker-db-$owner"; $image="localhost/hsr-worker-proof:$owner"
$folder=Join-Path $repoRoot "artifacts/worker-linux/$owner"
New-Item -ItemType Directory -Path $folder | Out-Null
$postgres='public.ecr.aws/supabase/postgres@sha256:16c0944c77885446d4f2ebbb2191b357feeb3b1295bde2fb1978c113129dbeb4'
$created=$false; $built=$false; $removed=$false; $imageRemoved=$false; $ready=$false; $stage='publish'; $errorCode=$null
$cases=[Collections.Generic.List[string]]::new()
$releaseCreated=$false
$script:executed=0; $script:failed=0
$expected=if($IsLinux){45}else{43}; $linuxSignalPerformed=$false
$recoveryResources=[Collections.Generic.List[string]]::new()
$names=@('POSTGRES_PASSWORD','HSR_WORKER_DATABASE','HSR_WORKER_IMAGE','HSR_WORKER_EVIDENCE','HSR_WORKER_RUNTIME','HSR_WORKER_SECCOMP',
    'HSR_CONTROL_PUBLISHABLE_KEY','HSR_CONTROL_EVIDENCE','HSR_CONTROL_PORT','HSR_CONTROL_ORIGIN','HSR_RELEASE_COMMIT')
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
    $stage='control-runtime'
    $testedControl=Join-Path $repoRoot 'artifacts/worker-build/cli'
    $env:HSR_CONTROL_PUBLISHABLE_KEY='sb_publishable_runtime_probe_only'
    $env:HSR_CONTROL_EVIDENCE=Join-Path $folder 'unused-control-evidence'
    $env:HSR_CONTROL_PORT='8086'; $env:HSR_CONTROL_ORIGIN='http://localhost:6086'; $env:HSR_RELEASE_COMMIT=$sourceCommit
    $controlText=@(& dotnet (Join-Path $testedControl 'HeadlessScreenReader.Cli.dll') --check-control 2>&1)
    if($LASTEXITCODE -ne 0){throw 'CONTROL_RUNTIME_PROBE_FAILED'}
    $control=$controlText -join "`n" | ConvertFrom-Json
    Assert-Proof ($control.Schema -eq 'hsr-control-runtime-proof-v1' -and $control.State -eq 'ControlRuntimeReady' -and
        $control.ReleaseCommit -eq $sourceCommit -and $control.PublicConfigurationRead -eq $true -and
        $control.UnauthenticatedSessionDenied -eq $true -and $control.ServerCloseConfirmed -eq $true -and
        $control.AppJwtValidated -eq $false -and $control.PublicMcpEnabled -eq $false) 'PublishedControlRuntimeChecksHttpGuardAndJoinsStopWithoutTenantClaim'
    $control | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $folder 'control-runtime.json') -Encoding utf8NoBOM
    $stage='image'
    $build=Invoke-ReaderRuntime $Runtime @('build','--tag',$image,'--label',"hsr.worker.proof=$owner",'--file',(Join-Path $repoRoot 'ops/worker/Containerfile'),$repoRoot) -AllowFailure
    $build.Text | Set-Content (Join-Path $folder 'build.log') -Encoding utf8NoBOM
    if($build.Code -ne 0){throw 'WORKER_IMAGE_BUILD_FAILED'}; $built=$true
    $imageId=(Invoke-ReaderRuntime $Runtime @('image','inspect','--format','{{.Id}}',$image)).Text.Trim()
    if($imageId -match '^[a-f0-9]{64}$'){$imageId='sha256:'+$imageId}
    if($imageId -notmatch '^sha256:[a-f0-9]{64}$'){throw 'WORKER_IMAGE_ID_UNCONFIRMED'}
    $stage='persistent-reader'
    $env:HSR_WORKER_IMAGE=$imageId; $env:HSR_WORKER_RUNTIME=$Runtime
    $env:HSR_WORKER_EVIDENCE=Join-Path $folder 'reader-state'
    $env:HSR_WORKER_SECCOMP=Join-Path $repoRoot 'ops/prototype/playwright-seccomp.json'
    $env:HSR_WORKER_DATABASE='deliberately invalid database configuration'
    $queueRoot=Join-Path $env:HSR_WORKER_EVIDENCE '.runtime'
    New-Item -ItemType Directory -Force $queueRoot | Out-Null
    $queueLock=[IO.FileStream]::new((Join-Path $queueRoot 'worker.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        $readerOutput=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --check-reader-runtime 2>&1)
        $readerExit=$LASTEXITCODE; $readerProbe=($readerOutput -join "`n") | ConvertFrom-Json
    } finally { $queueLock.Dispose() }
    $readerProbe | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $folder 'reader-runtime.json') -Encoding utf8NoBOM
    Assert-Proof ($readerExit -eq 0 -and $readerProbe.Schema -eq 'hsr-reader-runtime-readiness-v1' -and
        $readerProbe.State -eq 'ReaderRuntimeReady' -and $readerProbe.Image -eq $imageId -and
        $readerProbe.Captures -eq 3 -and $readerProbe.SameDocument -and $readerProbe.ObservedNodes -gt 0 -and
        $readerProbe.NativeShutdownConfirmed -and $readerProbe.ContainerAbsenceConfirmed -and
        $readerProbe.DuplicateDenied -and $readerProbe.CapacityDenied -and $readerProbe.CapacityReusableAfterConfirmedRemoval -and
        -not $readerProbe.DatabaseAccessed -and -not $readerProbe.ObservationPersisted -and
        $readerProbe.DispatchedInputs -eq 0 -and -not $readerProbe.GoalVerified) 'PersistentPackagedReaderRetainsDocumentAndCapacityUntilConfirmedNativeRemovalWithoutQueueLock'
    Assert-Proof ($readerProbe.ScopedRetirementConfirmed -and $readerProbe.WrongOrganizationDenied -and
        $readerProbe.SiblingDocumentPreserved) 'ScopedReaderRetirementDeniesWrongOrganizationAndPreservesAnotherLiveDocument'
    $readerJournal=Join-Path $env:HSR_WORKER_EVIDENCE '.reader-runtime'
    $readerRemovals=@(Get-ChildItem (Join-Path $readerJournal 'removals') -Filter '*.json' | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json })
    $readerRemaining=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter','name=^hsr-observation-','--format','{{.ID}}')
    Assert-Proof ($readerRemovals.Count -eq 3 -and @($readerRemovals | Where-Object {-not $_.ContainerAbsenceConfirmed -or $_.RecoveredAfterRestart}).Count -eq 0 -and
        @(Get-ChildItem (Join-Path $readerJournal 'intents')).Count -eq 0 -and [string]::IsNullOrWhiteSpace($readerRemaining.Text)) 'PersistentReaderRetiresThreeDurableIntentsOnlyAfterActualContainerAbsence'
    $env:HSR_WORKER_IMAGE='sha256:'+('f'*64)
    $readerOutput=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --check-reader-runtime 2>&1)
    $readerExit=$LASTEXITCODE; $env:HSR_WORKER_IMAGE=$imageId
    Assert-Proof ($readerExit -eq 3 -and ($readerOutput -join "`n").Trim() -eq 'WORKER_STOPPED_UNCONFIRMED' -and
        @(Get-ChildItem (Join-Path $readerJournal 'intents')).Count -eq 0) 'MissingReaderImageCannotReturnReadyAndRetiresIntentOnlyAfterNativeAbsence'
    $readerOwner=[Guid]::NewGuid(); $readerTarget="hsr-observation-$($readerOwner.ToString('N'))"
    $readerDescriptor=@{OrganizationId="$([Guid]::NewGuid())";ScanId="$([Guid]::NewGuid())";JobId="$([Guid]::NewGuid())";Generation=1}
    $readerIntent=@{Schema='hsr-native-ownership-v1';Owner="$readerOwner";Job=$readerDescriptor;Runtime=$Runtime;Image=$imageId}
    $readerIntentPath=Join-Path $readerJournal "intents/$($readerOwner.ToString('N')).json"
    $readerIntent | ConvertTo-Json -Depth 4 | Set-Content $readerIntentPath -Encoding utf8NoBOM
    $readerLabels=@('--label',"hsr.observation.owner=$($readerOwner.ToString('N'))",'--label',"hsr.observation.organization=$($readerDescriptor.OrganizationId.Replace('-',''))",'--label',"hsr.observation.scan=$($readerDescriptor.ScanId.Replace('-',''))",'--label',"hsr.observation.job=$($readerDescriptor.JobId.Replace('-',''))",'--label','hsr.observation.generation=1','--label',"hsr.worker.proof=$owner")
    $null=Invoke-ReaderRuntime $Runtime (@('create','--interactive','--name',$readerTarget,'--network','none','--user','1001:1001','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges','--entrypoint','dotnet')+$readerLabels+@($imageId,'/app/cli/HeadlessScreenReader.Cli.dll','--reader-fixture'))
    $recoveryResources.Add($readerTarget)
    $readerOutput=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --recover-reader 2>&1)
    $readerExit=$LASTEXITCODE; $readerRecovery=($readerOutput -join "`n") | ConvertFrom-Json
    $readerGone=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter',"name=^$readerTarget`$",'--format','{{.ID}}')
    $readerRemoval=Get-Content (Join-Path $readerJournal "removals/$($readerOwner.ToString('N')).json") -Raw | ConvertFrom-Json
    Assert-Proof ($readerExit -eq 0 -and $readerRecovery.Schema -eq 'hsr-reader-recovery-v1' -and $readerRecovery.State -eq 'ReaderRecovered' -and
        $readerRecovery.Recovered -eq 1 -and [string]::IsNullOrWhiteSpace($readerGone.Text) -and -not (Test-Path $readerIntentPath) -and
        $readerRemoval.ContainerAbsenceConfirmed -and $readerRemoval.RecoveredAfterRestart -and $readerRemoval.Intent.Owner -eq "$readerOwner") 'SeparateReaderRecoveryRemovesOnlyItsRecordedContainerWithoutDatabaseAccess'
    $stage='isolated-reader-http'
    $beforeHttpRemovals=@(Get-ChildItem (Join-Path $readerJournal 'removals') -Filter '*.json').Count
    $queueLock=[IO.FileStream]::new((Join-Path $queueRoot 'worker.lock'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        $httpOutput=@(& dotnet (Join-Path $testedControl 'HeadlessScreenReader.Cli.dll') --check-isolated-reader-api 2>&1)
        $httpExit=$LASTEXITCODE
    } finally { $queueLock.Dispose() }
    $httpProbe=($httpOutput -join "`n") | ConvertFrom-Json
    $httpProbe | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $folder 'isolated-reader-http.json') -Encoding utf8NoBOM
    Assert-Proof ($httpExit -eq 0 -and $httpProbe.Schema -eq 'hsr-isolated-reader-http-proof-v1' -and
        $httpProbe.State -eq 'IsolatedReaderHttpReady' -and $httpProbe.Image -eq $imageId -and $httpProbe.SameDocument -and
        $httpProbe.FreshHttpObservations -eq 2 -and $httpProbe.FreshNavigationCaptures -eq 2 -and
        $httpProbe.ReaderCursorProgressionConfirmed -and $httpProbe.PagedCommandTimelineConfirmed -and
        $httpProbe.StateEdgeTraceConfirmed -and
        $httpProbe.InitialNativeCaptures -eq 1 -and $httpProbe.ObservedNodes -gt 0 -and
        $httpProbe.AnonymousDenied -and $httpProbe.OtherSubjectDenied -and $httpProbe.RevokedWorkspaceDenied -and $httpProbe.StaleSessionDenied -and
        $httpProbe.NativeShutdownConfirmed -and $httpProbe.ContainerAbsenceConfirmed -and $httpProbe.NativeExecutionOutsideApi -and
        $httpProbe.GlobalSubjectReservationConfirmed -and $httpProbe.GlobalCapacityConfirmed -and $httpProbe.WorkerScopeDenied -and
        $httpProbe.SiblingDocumentPreserved -and $httpProbe.SyntheticIdentityOnly -and
        -not $httpProbe.AppJwtValidated -and -not $httpProbe.HostedTenantReaderAccepted -and -not $httpProbe.DatabaseAccessed -and
        -not $httpProbe.ObservationPersisted -and $httpProbe.DispatchedInputs -eq 0 -and -not $httpProbe.GoalVerified) 'PublishedHttpReaderUsesIsolatedNativeContextAndRechecksSyntheticAuthority'
    $afterHttpRemovals=@(Get-ChildItem (Join-Path $readerJournal 'removals') -Filter '*.json').Count
    $httpRemaining=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter','name=^hsr-observation-','--format','{{.ID}}')
    $readerLock=[IO.FileStream]::new((Join-Path $readerJournal 'worker.lock'),[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        Assert-Proof ($readerLock.CanWrite -and $afterHttpRemovals -eq $beforeHttpRemovals+2 -and
            @(Get-ChildItem (Join-Path $readerJournal 'intents')).Count -eq 0 -and -not (Test-Path (Join-Path $readerJournal 'host.json')) -and
            [string]::IsNullOrWhiteSpace($httpRemaining.Text)) 'HttpReaderRetiresIntentAndReleasesExclusiveJournalAfterServerDrain'
    } finally { $readerLock.Dispose() }
    $stage='s3-evidence'
    $s3=& (Join-Path $PSScriptRoot 'Test-S3Evidence.ps1') -Runtime $Runtime -Image $imageId -PublishedCli $testedControl -Folder (Join-Path $folder 's3')
    Assert-Proof ($s3.Ready -and $s3.Passed -eq 10 -and $s3.ServerRemoved -and $s3.NetworkRemoved -and $s3.NativeOwnershipRecovered) 'ActualSignedS3FinalizationAndOffHostReadbackPreserveNativeFacts'
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
    $realConnection=$env:HSR_WORKER_DATABASE
    $env:HSR_WORKER_DATABASE='deliberately invalid database configuration'
    $probeOutput=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --check-runtime 2>&1)
    $probeExit=$LASTEXITCODE; $runtimeProbe=($probeOutput -join "`n") | ConvertFrom-Json
    Assert-Proof ($probeExit -eq 0 -and $runtimeProbe.Schema -eq 'hsr-runtime-readiness-v1' -and $runtimeProbe.State -eq 'RuntimeReady' -and $runtimeProbe.Image -eq $imageId -and $runtimeProbe.ObservedNodes -gt 0 -and $runtimeProbe.Sha256 -cmatch '^[a-f0-9]{64}$' -and -not $runtimeProbe.DatabaseAccessed -and -not $runtimeProbe.ObservationPersisted -and $runtimeProbe.DispatchedInputs -eq 0 -and -not $runtimeProbe.GoalVerified) 'RuntimeReadinessExecutesActualChromiumWithoutDatabaseConfiguration'
    $probeFacts=(Invoke-ProofSql "select json_build_object('attempts',(select count(*) from hsr.job_attempts),'queue',(select count(*) from pgmq.q_hsr_scan_jobs),'state',(select state from hsr.scans where id='$($job.scan_id)'))") | ConvertFrom-Json
    Assert-Proof ($probeFacts.attempts -eq 0 -and $probeFacts.queue -eq 1 -and $probeFacts.state -eq 'queued' -and @(Get-ChildItem $env:HSR_WORKER_EVIDENCE -Recurse -Filter 'observation.json').Count -eq 0) 'RuntimeReadinessCannotAdmitClaimOrPersistAnObservation'
    $probeRemaining=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter','name=^hsr-observation-','--format','{{.ID}}')
    Assert-Proof ([string]::IsNullOrWhiteSpace($probeRemaining.Text)) 'RuntimeReadinessConfirmsActualOwnedContainerAbsence'
    $env:HSR_WORKER_IMAGE='sha256:'+('f'*64)
    $probeOutput=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --check-runtime 2>&1)
    $probeExit=$LASTEXITCODE
    Assert-Proof ($probeExit -eq 3 -and ($probeOutput -join "`n").Trim() -eq 'WORKER_STOPPED_UNCONFIRMED') 'MissingImmutableRuntimeImageCannotProduceReady'
    $env:HSR_WORKER_DATABASE=$realConnection; $env:HSR_WORKER_IMAGE=$imageId
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
    $producer=(Invoke-ProofSql "select json_build_object('index',j.archive_index_sha256,'manifest',j.archive_manifest_sha256,'bytes',j.archive_bytes,'worker',j.archive_worker_id,'actual_worker',a.worker_id,'generation',a.generation) from hsr.scan_jobs j join hsr.job_attempts a on a.organization_id=j.organization_id and a.job_id=j.id and a.generation=j.generation where j.id='$($job.job_id)'" ) | ConvertFrom-Json
    Assert-Proof ($producer.index -cmatch '^[a-f0-9]{64}$' -and $producer.manifest -cmatch '^[a-f0-9]{64}$' -and $producer.worker -eq $producer.actual_worker -and $producer.generation -eq 1) 'FencedCompletionPublishesArchiveForActualWorkerAttempt'
    $archiveRoot=Join-Path $env:HSR_WORKER_EVIDENCE "archives/traversal-v1/$(([Guid]$org).ToString('N'))/$(([Guid]$job.job_id).ToString('N'))/$($producer.index)"
    $indexPath=Join-Path $archiveRoot 'index.json'; $index=Get-Content $indexPath -Raw | ConvertFrom-Json
    $original=@($index.Files | Where-Object Path -CEQ 'observation.json'); $manifest=@($index.Files | Where-Object Path -CEQ 'evidence-manifest.json')
    $originalPath=if($original.Count -eq 1 -and $original[0].Parts.Count -eq 1){Join-Path $archiveRoot "objects/$($original[0].Parts[0].Sha256)"}else{throw 'WORKER_ARCHIVE_ORIGINAL_UNCONFIRMED'}
    Assert-Proof ((Get-FileHash $indexPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $producer.index -and $index.OrganizationId -eq $org -and $index.ArchiveId -eq $job.job_id -and $index.SourceManifestSha256 -ceq $producer.manifest -and $index.Files.Count -eq 2 -and $original[0].Sha256 -ceq $facts.digest -and $original[0].Bytes -eq $bytes.Length -and (Get-Item $originalPath).Length -eq $bytes.Length -and (Get-FileHash $originalPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $facts.digest -and $producer.bytes -eq ($index.Files | Measure-Object Bytes -Sum).Sum) 'ProducerArchiveIndexBindsJobAndExactOriginalBytes'
    if($manifest.Count -ne 1 -or $manifest[0].Parts.Count -ne 1){throw 'WORKER_ARCHIVE_MANIFEST_UNCONFIRMED'}
    $manifestPath=Join-Path $archiveRoot "objects/$($manifest[0].Parts[0].Sha256)"; $source=Get-Content $manifestPath -Raw | ConvertFrom-Json
    Assert-Proof ((Get-FileHash $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $producer.manifest -and $source.Schema -ceq 'hsr-worker-observation-package-v1' -and $source.Job.OrganizationId -eq $org -and $source.Job.ScanId -eq $job.scan_id -and $source.Job.JobId -eq $job.job_id -and $source.Job.Generation -eq 1 -and $source.WorkerId -eq $producer.actual_worker -and $source.ObservationSha256 -ceq $facts.digest -and $source.Origin -ceq 'QueueWorkerObservation' -and $source.Recording -ceq 'NotCollected' -and $source.DispatchedInputs -eq 0 -and -not $source.GoalVerified) 'ProducerManifestPreservesNoRecordingNoInputAndActualAttemptScope'
    $remaining=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter','name=^hsr-observation-','--format','{{.ID}}')
    Assert-Proof ([string]::IsNullOrWhiteSpace($remaining.Text)) 'OwnedObservationContainerIsRemovedBeforeCompletion'
    $stage='passive-recording'
    $recordRequest=[Guid]::NewGuid()
    $admission=Invoke-ProofSql "begin; set local role authenticated; select set_config('request.jwt.claim.sub','$user',true); select row_to_json(r) from hsr.create_recorded_observation('$org','$recordRequest') r; commit;"
    $recordJob=($admission -split "`n" | Where-Object {$_ -match '^\{'}) | ConvertFrom-Json
    $output=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --once 2>&1)
    $recordExit=$LASTEXITCODE; $recordResult=($output -join "`n") | ConvertFrom-Json
    $recordOutput=$output -join "`n"; $recordOutput | Set-Content (Join-Path $folder 'passive-worker.log') -Encoding utf8NoBOM
    $recordFacts=(Invoke-ProofSql "select json_build_object('state',j.state,'mode',j.record_observation,'index',j.archive_index_sha256,'manifest',j.archive_manifest_sha256,'bytes',j.archive_bytes,'digest',j.result_digest,'worker',j.archive_worker_id,'actual_worker',a.worker_id) from hsr.scan_jobs j join hsr.job_attempts a on a.organization_id=j.organization_id and a.job_id=j.id and a.generation=j.generation where j.id='$($recordJob.job_id)'" ) | ConvertFrom-Json
    Assert-Proof ($recordExit -eq 0 -and $recordResult.State -eq 'Observed' -and $recordResult.JobId -eq $recordJob.job_id -and $recordFacts.mode -and $recordFacts.state -eq 'completed' -and $recordFacts.worker -eq $recordFacts.actual_worker) 'RealWorkerProducesExplicitPassiveRecordingAfterAdmittedClaim'
    $recordPath=Join-Path $env:HSR_WORKER_EVIDENCE $recordResult.Reference; $recordObservation=Get-Content $recordPath -Raw | ConvertFrom-Json
    Assert-Proof ($recordObservation.Job.RecordObservation -and $recordObservation.Job.JobId -eq $recordJob.job_id -and $recordObservation.FixtureSha256 -ceq 'b49eef1ff4f1e225ea263fc559c2e4ab5ea75a75a3c6ee091b57980e210a0d7b' -and $recordObservation.DispatchedInputs -eq 0 -and -not $recordObservation.GoalVerified -and @($recordObservation.Snapshot.Nodes | Where-Object Role -In @('textbox','code')).Count -eq 0 -and $recordObservation.Shutdown.ProcessExitConfirmed -and $recordObservation.Shutdown.CollectorStopAcknowledged) 'PassiveRecordedObservationUsesSanitizedFixtureAndZeroInput'
    $recordArchive=Join-Path $env:HSR_WORKER_EVIDENCE "archives/traversal-v1/$(([Guid]$org).ToString('N'))/$(([Guid]$recordJob.job_id).ToString('N'))/$($recordFacts.index)"
    $recordIndexPath=Join-Path $recordArchive 'index.json'; $recordIndex=Get-Content $recordIndexPath -Raw | ConvertFrom-Json
    Assert-Proof ((Get-FileHash $recordIndexPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $recordFacts.index -and $recordIndex.Files.Count -eq 4 -and $recordIndex.SourceManifestSha256 -ceq $recordFacts.manifest -and $recordFacts.bytes -eq ($recordIndex.Files | Measure-Object Bytes -Sum).Sum -and @($recordIndex.Files | Where-Object Path -CEQ 'original.webm').Count -eq 1 -and @($recordIndex.Files | Where-Object Path -CEQ 'capture-state.json').Count -eq 1) 'PassiveArchiveRetainsFourOriginalsWithIndexLastHashes'
    $videoEntry=$recordIndex.Files | Where-Object Path -CEQ 'original.webm'; $captureEntry=$recordIndex.Files | Where-Object Path -CEQ 'capture-state.json'
    $capturePath=Join-Path $recordArchive "objects/$($captureEntry.Parts[0].Sha256)"; $recordCapture=Get-Content $capturePath -Raw | ConvertFrom-Json
    $videoBuffer=[IO.MemoryStream]::new(); $partsValid=$true
    try { foreach($part in $videoEntry.Parts){$partPath=Join-Path $recordArchive "objects/$($part.Sha256)"; $partBytes=[IO.File]::ReadAllBytes($partPath); $partsValid=$partsValid -and $partBytes.Length -eq $part.Bytes -and (Get-FileHash $partPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $part.Sha256; $videoBuffer.Write($partBytes)}; $videoBytes=$videoBuffer.ToArray() } finally {$videoBuffer.Dispose()}
    $videoDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($videoBytes)).ToLowerInvariant()
    Assert-Proof ($partsValid -and $videoBytes.Length -eq $videoEntry.Bytes -and $videoDigest -ceq $videoEntry.Sha256 -and $recordCapture.VideoSha256 -ceq $videoDigest -and $recordCapture.VideoBytes -eq $videoBytes.Length -and $videoBytes[0] -eq 0x1a -and $videoBytes[1] -eq 0x45 -and $videoBytes[2] -eq 0xdf -and $videoBytes[3] -eq 0xa3 -and $recordCapture.DecodedVideoFrames -gt 0) 'PassiveOriginalVideoMatchesDecodedCollectorReceipt'
    Assert-Proof ((Get-FileHash $capturePath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $captureEntry.Sha256 -and $recordCapture.Schema -ceq 'hsr-passive-observation-recording-v1' -and $recordCapture.Job.JobId -eq $recordJob.job_id -and $recordCapture.Job.RecordObservation -and $recordCapture.States.Count -eq 1 -and $recordCapture.Edges.Count -eq 0 -and $recordCapture.States[0].DocumentId -ceq $recordObservation.Snapshot.DocumentId -and $recordCapture.States[0].ObservationSha256 -ceq $recordFacts.digest -and $recordCapture.Capture.Phase -ceq 'Closed' -and @($recordCapture.Capture.Events | Where-Object Phase -CEQ 'Excluded').Count -gt 0 -and $recordCapture.Timing -ceq 'NotAlignedToSnapshot') 'PassiveGraphRetainsSingleActualCaptureAndZeroInputEdges'
    $remaining=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter','name=^hsr-observation-','--format','{{.ID}}')
    Assert-Proof ([string]::IsNullOrWhiteSpace($remaining.Text)) 'PassiveCaptureHasNoRemainingOwnedContainerBeforeCompletion'
    $stage='keyboard-recording'
    $keyboardRequest=[Guid]::NewGuid()
    $admission=Invoke-ProofSql "begin; set local role authenticated; select set_config('request.jwt.claim.sub','$user',true); select row_to_json(r) from hsr.create_recorded_traversal('$org','$keyboardRequest',1) r; commit;"
    $keyboardJob=($admission -split "`n" | Where-Object {$_ -match '^\{'}) | ConvertFrom-Json
    $output=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --once 2>&1)
    $keyboardExit=$LASTEXITCODE; $keyboardResult=($output -join "`n") | ConvertFrom-Json
    $output -join "`n" | Set-Content (Join-Path $folder 'keyboard-worker.log') -Encoding utf8NoBOM
    $keyboardFacts=(Invoke-ProofSql "select json_build_object('state',j.state,'route',j.traversal_route,'index',j.archive_index_sha256,'manifest',j.archive_manifest_sha256,'bytes',j.archive_bytes,'worker',j.archive_worker_id,'actual_worker',a.worker_id) from hsr.scan_jobs j join hsr.job_attempts a on a.organization_id=j.organization_id and a.job_id=j.id and a.generation=j.generation where j.id='$($keyboardJob.job_id)'" ) | ConvertFrom-Json
    Assert-Proof ($keyboardExit -eq 0 -and $keyboardResult.State -eq 'Observed' -and $keyboardResult.JobId -eq $keyboardJob.job_id -and $keyboardFacts.route -eq 1 -and $keyboardFacts.state -eq 'completed' -and $keyboardFacts.worker -eq $keyboardFacts.actual_worker) 'RealAdmittedKeyboardWorkerRetainsRouteAndActualAttempt'
    $keyboardBaseline=Get-Content (Join-Path $env:HSR_WORKER_EVIDENCE $keyboardResult.Reference) -Raw | ConvertFrom-Json
    $keyboardArchive=Join-Path $env:HSR_WORKER_EVIDENCE "archives/traversal-v1/$(([Guid]$org).ToString('N'))/$(([Guid]$keyboardJob.job_id).ToString('N'))/$($keyboardFacts.index)"
    $keyboardIndexPath=Join-Path $keyboardArchive 'index.json'; $keyboardIndex=Get-Content $keyboardIndexPath -Raw|ConvertFrom-Json
    function Read-KeyboardOriginal($entry) {
        $buffer=[IO.MemoryStream]::new()
        try {foreach($part in $entry.Parts){$path=Join-Path $keyboardArchive "objects/$($part.Sha256)"; $bytes=[IO.File]::ReadAllBytes($path); if($bytes.Length -ne $part.Bytes -or (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $part.Sha256){throw 'KEYBOARD_PART_UNCONFIRMED'}; $buffer.Write($bytes)}; $all=$buffer.ToArray()} finally {$buffer.Dispose()}
        if($all.Length -ne $entry.Bytes -or [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($all)).ToLowerInvariant() -cne $entry.Sha256){throw 'KEYBOARD_FILE_UNCONFIRMED'}
        return ,$all
    }
    $keyboardData=[Text.Encoding]::UTF8.GetString((Read-KeyboardOriginal ($keyboardIndex.Files|Where-Object Path -CEQ 'traversal.json'))) | ConvertFrom-Json
    $keyboardProducer=[Text.Encoding]::UTF8.GetString((Read-KeyboardOriginal ($keyboardIndex.Files|Where-Object Path -CEQ 'evidence-manifest.json'))) | ConvertFrom-Json
    Assert-Proof ($keyboardBaseline.Job.TraversalRoute -eq 1 -and $keyboardBaseline.DispatchedInputs -eq 0 -and -not $keyboardBaseline.GoalVerified -and $keyboardData.Receipt.CompletedKeystrokes -eq 2 -and $keyboardData.Receipt.AttemptedKeystrokes -eq 2 -and $keyboardData.Receipt.Reachability -ceq 'Reached') 'KeyboardBaselineCannotReplaceActualInputAndOutcome'
    Assert-Proof ((Get-FileHash $keyboardIndexPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $keyboardFacts.index -and $keyboardIndex.Files.Count -eq 5 -and $keyboardProducer.Origin -ceq 'QueueWorkerTraversal' -and $keyboardProducer.WorkerId -eq $keyboardFacts.actual_worker -and $keyboardProducer.CompletedKeystrokes -eq 2 -and $keyboardData.CollectorSha256 -match '^[a-f0-9]{64}$') 'KeyboardArchiveRetainsFiveOriginalsAndCollectorIdentity'
    Assert-Proof ($keyboardData.Graph.Nodes.Count -eq 3 -and $keyboardData.Graph.Edges.Count -eq 2 -and @($keyboardData.Graph.Edges|Where-Object { $_.InputEvidenceComplete -and $_.KeyboardInputs.Count -eq 1 -and $null -ne $_.KeyboardInputs[0].AttemptEventSequence -and $null -ne $_.KeyboardInputs[0].CompletionEventSequence }).Count -eq 2) 'KeyboardGraphRetainsActualSourceActionResultAndCompletedKeys'
    $keyboardVideo=Read-KeyboardOriginal ($keyboardIndex.Files|Where-Object Path -CEQ 'original.webm'); $keyboardFrames=Read-KeyboardOriginal ($keyboardIndex.Files|Where-Object Path -CEQ 'source-frames.zip')
    Assert-Proof ($keyboardVideo[0] -eq 0x1a -and $keyboardFrames[0] -eq 0x50 -and $keyboardData.DecodedVideoFrames -gt 0 -and $keyboardData.SourceFiles.Count -gt 0 -and $keyboardData.VideoFrames.Count -eq $keyboardData.DecodedVideoFrames -and $keyboardData.ClockSamples.Count -eq 14 -and $keyboardData.Alignment.RunId -eq $keyboardData.Receipt.RunId -and $keyboardData.Capture.Phase -ceq 'Closed' -and @($keyboardData.Capture.Events|Where-Object Phase -CEQ 'Excluded').Count -gt 0) 'KeyboardRecordingRetainsOriginalFramesMeasuredTimingAndClosedCapture'
    $remaining=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter','name=^hsr-observation-','--format','{{.ID}}')
    Assert-Proof ([string]::IsNullOrWhiteSpace($remaining.Text)) 'KeyboardContainerAbsentBeforeCompletion'
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
    Assert-Proof ($unchanged.jobs -eq 4 -and $unchanged.attempts -eq 4 -and $unchanged.completed -eq 3 -and $unchanged.queue -eq 0 -and @(Get-ChildItem $env:HSR_WORKER_EVIDENCE -Recurse -Filter observation.json).Count -eq 0) 'NativeRecoveryNeedsNoDatabaseCredentialAndCannotInventObservation'
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
    if($PublishRelease){
        $stage='release-bundle'
        if(@(& git -C $repoRoot status --porcelain).Count -ne 0){throw 'WORKER_RELEASE_REQUIRES_COMMITTED_SOURCE'}
        if((& git -C $repoRoot rev-parse HEAD).Trim() -ne $sourceCommit){throw 'WORKER_RELEASE_SOURCE_CHANGED'}
        $releaseRoot=Join-Path $repoRoot 'artifacts/private-worker-release'
        if(Test-Path $releaseRoot){throw 'WORKER_RELEASE_OUTPUT_EXISTS'}
        $content=Join-Path $releaseRoot 'content'
        New-Item -ItemType Directory -Path $content | Out-Null
        $archive=Join-Path $content 'fixture.tar'
        $saveArgs=if($Runtime -eq 'podman'){@('save','--format','docker-archive','--output',$archive,$imageId)}else{@('save','--output',$archive,$imageId)}
        $null=Invoke-ReaderRuntime $Runtime $saveArgs
        $testedWorker=Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0'
        $releaseWorker=Join-Path $content 'worker'
        New-Item -ItemType Directory -Path $releaseWorker | Out-Null
        foreach($file in Get-ChildItem -LiteralPath $testedWorker){Copy-Item -LiteralPath $file.FullName -Destination $releaseWorker -Recurse}
        $releaseControl=Join-Path $content 'control'
        New-Item -ItemType Directory -Path $releaseControl | Out-Null
        foreach($file in Get-ChildItem -LiteralPath $testedControl -Force){Copy-Item -LiteralPath $file.FullName -Destination $releaseControl -Recurse -Force}
        $commit=$sourceCommit
        $pythonCommand=if($IsWindows){'python'}else{'python3'}
        & $pythonCommand (Join-Path $repoRoot 'ops/worker/build_release.py') --stage $content --output (Join-Path $releaseRoot 'release.tar.gz') --commit $commit --image $imageId --tested-worker $testedWorker --tested-control $testedControl
        if($LASTEXITCODE -ne 0){throw 'WORKER_RELEASE_BINDING_UNCONFIRMED'}
        $releaseCreated=$true
    }
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
$receipt=@{Schema='hsr-observation-worker-proof-v1';Ready=$ready;Stage=$stage;ErrorCode=$errorCode;Runtime=$Runtime;Expected=$expected;Discovered=$expected;Executed=$script:executed;Passed=$cases.Count;Failed=$script:failed;Unconfirmed=$expected-$script:executed;Skipped=0;Cases=@($cases);DatabaseRemoved=$removed;ImageRemoved=$imageRemoved;LinuxSignalPerformed=$linuxSignalPerformed;HostedProjectChanged=$false;ReleaseBundleCreated=$releaseCreated;Folder=$folder}
$receipt | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $folder 'run.json') -Encoding utf8NoBOM
$receipt | ConvertTo-Json -Depth 4
if(-not $ready -or -not $removed -or -not $imageRemoved -or $cases.Count -ne $expected){exit 1}
