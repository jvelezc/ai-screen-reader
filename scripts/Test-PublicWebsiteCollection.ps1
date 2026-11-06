param([ValidateSet('podman','docker')][string]$Runtime='podman',[Parameter(Mandatory)][string]$Image,[string]$DotNet='dotnet')
$ErrorActionPreference='Stop'
if($Image -cnotmatch '^sha256:[a-f0-9]{64}$'){throw 'PUBLIC_COLLECTION_PINNED_IMAGE_REQUIRED'}
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$repoRoot=Split-Path $PSScriptRoot -Parent
$owner=[Guid]::NewGuid().ToString('N');$name='hsr-public-db-'+$owner
$folder=Join-Path $repoRoot ('artifacts/public-collection/'+$owner)
$evidence=Join-Path $folder 'evidence'
$null=New-Item -ItemType Directory -Path $folder
$created=$false;$removed=$false;$ready=$false;$stage='database';$facts=$null;$failureFacts=$null;$workerResult=$null;$nativeAbsent=$false
$names=@('POSTGRES_PASSWORD','HSR_WORKER_DATABASE','HSR_WORKER_IMAGE','HSR_WORKER_RUNTIME','HSR_WORKER_EVIDENCE','HSR_WORKER_SECCOMP','HSR_EVIDENCE_S3_ACCESS_KEY','HSR_EVIDENCE_S3_SECRET')
$prior=@{};foreach($key in $names){$prior[$key]=[Environment]::GetEnvironmentVariable($key)}
function Invoke-PublicProofSql([string]$query){
    $lines=@($query | & $Runtime exec --interactive $name psql -X -U postgres -d postgres -qAt -v ON_ERROR_STOP=1 2>&1)
    if($LASTEXITCODE -ne 0){throw 'PUBLIC_COLLECTION_DATABASE_SQL_UNCONFIRMED'}
    return $lines -join "`n"
}
try {
    $postgres=Resolve-ReaderDatabaseImage $Runtime
    $env:POSTGRES_PASSWORD=[Guid]::NewGuid().ToString('N')
    $null=Invoke-ReaderRuntime $Runtime @('create','--name',$name,'--publish','127.0.0.1::5432','--memory','768m','--cpus','2','--pids-limit','128','--label',"hsr.public.collection=$owner",'--env','POSTGRES_PASSWORD',$postgres)
    $created=$true;$null=Invoke-ReaderRuntime $Runtime @('start',$name)
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do {
        $root=Invoke-ReaderRuntime $Runtime @('exec',$name,'cat','/proc/1/cmdline') -AllowFailure
        $probe=Invoke-ReaderRuntime $Runtime @('exec',$name,'pg_isready','-U','postgres','-d','postgres') -AllowFailure
        if($root.Code -eq 0 -and $root.Text.Split([char]0)[0] -match '(^|/)postgres$' -and $probe.Code -eq 0){break}
        if($clock.Elapsed.TotalSeconds -ge 60){throw 'PUBLIC_COLLECTION_DATABASE_START_UNCONFIRMED'}
        Start-Sleep -Milliseconds 250
    }while($true)
    $stage='migration'
    foreach($migration in Get-ChildItem (Join-Path $repoRoot 'supabase/migrations') -Filter '*.sql'|Sort-Object Name){
        $null=Invoke-ReaderRuntime $Runtime @('cp',$migration.FullName,"${name}:/tmp/migration.sql")
        $null=Invoke-ReaderRuntime $Runtime @('exec',$name,'psql','-X','-U','postgres','-d','postgres','-qAt','-v','ON_ERROR_STOP=1','--single-transaction','-f','/tmp/migration.sql')
    }
    $user=[Guid]::NewGuid();$org=[Guid]::NewGuid();$request=[Guid]::NewGuid();$password=[Guid]::NewGuid().ToString('N')
    $null=Invoke-PublicProofSql "create role hsr_public_proof_worker login password '$password';grant hsr_worker to hsr_public_proof_worker;insert into auth.users(id) values('$user');insert into hsr.organizations(id,name) values('$org','Public source acceptance');insert into hsr.memberships values('$org','$user');"
    $stage='admission'
    $admission=Invoke-PublicProofSql "begin;set local role authenticated;select set_config('request.jwt.claim.sub','$user',true);select row_to_json(r) from hsr.create_public_scan('$org','$request','https://screenreader.privensic.com/ci/v1/source','screenreader.privensic.com') r;commit;"
    $job=($admission -split "`n"|Where-Object {$_ -match '^\{'})|ConvertFrom-Json
    if(-not $job.job_id -or -not $job.scan_id){throw 'PUBLIC_COLLECTION_ADMISSION_UNCONFIRMED'}
    $port=(Invoke-ReaderRuntime $Runtime @('port',$name,'5432/tcp')).Text.Trim()
    if($port -notmatch '^127\.0\.0\.1:([0-9]+)$'){throw 'PUBLIC_COLLECTION_DATABASE_PORT_UNCONFIRMED'}
    $env:HSR_WORKER_DATABASE="Host=127.0.0.1;Port=$($Matches[1]);Username=hsr_public_proof_worker;Password=$password;Database=postgres;SSL Mode=Disable"
    $env:HSR_WORKER_IMAGE=$Image;$env:HSR_WORKER_RUNTIME=$Runtime;$env:HSR_WORKER_EVIDENCE=$evidence
    $env:HSR_WORKER_SECCOMP=Join-Path $repoRoot 'ops/prototype/playwright-seccomp.json'
    $env:HSR_EVIDENCE_S3_ACCESS_KEY=$null;$env:HSR_EVIDENCE_S3_SECRET=$null
    $stage='collection'
    # Operational JSON lines are stderr, never part of the single stdout result.
    $lines=@(& $DotNet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --once-public 2> (Join-Path $folder 'worker.stderr.jsonl'))
    $workerExit=$LASTEXITCODE
    $lines|Set-Content (Join-Path $folder 'worker.json') -Encoding utf8NoBOM
    $workerResult=($lines -join "`n")|ConvertFrom-Json
    if($workerExit -ne 0 -or $workerResult.State -ne 'Observed' -or $workerResult.JobId -ne $job.job_id){throw 'PUBLIC_COLLECTION_EXECUTION_UNCONFIRMED'}
    $stage='retained-result'
    $facts=(Invoke-PublicProofSql "select json_build_object('State',j.state,'Source',s.target_url,'Profile',s.reader_profile,'Generation',j.generation,'DigestMatches',j.result_digest=encode(sha256(convert_to(j.result_text,'UTF8')),'hex'),'ProducerMatches',j.result_text::jsonb->>'WorkerId'=j.result_worker_id::text,'JobMatches',j.result_text::jsonb->'Job'->>'JobId'=j.id::text,'OrganizationMatches',j.result_text::jsonb->'Job'->>'OrganizationId'=j.organization_id::text,'Captures',jsonb_array_length(j.result_text::jsonb->'Captures'),'Nodes',jsonb_array_length(j.result_text::jsonb->'Captures'->1->'Nodes'),'RootObserved',exists(select 1 from jsonb_array_elements(j.result_text::jsonb->'Captures'->1->'Nodes') n where n->>'Role'='RootWebArea' and n->>'Name'='Reader CI source v1'),'NativeClosed',j.result_text::jsonb->'NativeShutdownConfirmed','ResourcesAbsent',j.result_text::jsonb->'ResourcesAbsenceConfirmed','Inputs',j.result_text::jsonb->'DispatchedInputs','Goal',j.result_text::jsonb->'GoalVerified','Leases',(select count(*) from hsr.public_scan_leases),'Queue',(select count(*) from pgmq.q_hsr_public_scan_jobs),'Bytes',octet_length(j.result_text)) from hsr.public_scan_jobs j join hsr.public_scans s on s.organization_id=j.organization_id and s.id=j.scan_id where j.organization_id='$org' and j.id='$($job.job_id)'")|ConvertFrom-Json
    if($facts.State -ne 'completed' -or $facts.Source -cne 'https://screenreader.privensic.com/ci/v1/source' -or $facts.Profile -cne 'hsr-approved-public-readonly-v1' -or
        $facts.Generation -ne 1 -or -not $facts.DigestMatches -or -not $facts.ProducerMatches -or -not $facts.JobMatches -or -not $facts.OrganizationMatches -or
        $facts.Captures -ne 2 -or $facts.Nodes -ne 9 -or -not $facts.RootObserved -or -not $facts.NativeClosed -or -not $facts.ResourcesAbsent -or
        $facts.Inputs -ne 0 -or $facts.Goal -or $facts.Leases -ne 0 -or $facts.Queue -ne 0){throw 'PUBLIC_COLLECTION_RETAINED_FACTS_UNCONFIRMED'}
    $stage='native-absence'
    $journal=Join-Path $evidence '.public-reader-runtime'
    $ack=@(Get-ChildItem -LiteralPath (Join-Path $journal 'removed') -Filter '*.json'|ForEach-Object {Get-Content $_.FullName -Raw|ConvertFrom-Json})
    if($ack.Count -ne 1 -or @(Get-ChildItem -LiteralPath (Join-Path $journal 'intents')).Count -ne 0 -or
        -not $ack[0].ContainersAbsenceConfirmed -or -not $ack[0].NetworksAbsenceConfirmed){throw 'PUBLIC_COLLECTION_OWNERSHIP_UNCONFIRMED'}
    $prefix='hsr-public-'+([Guid]$ack[0].Intent.Owner).ToString('N')+'-'
    $containers=(Invoke-ReaderRuntime $Runtime @('ps','--all','--format','{{.Names}}')).Text -split "`n"
    $networks=(Invoke-ReaderRuntime $Runtime @('network','ls','--format','{{.Name}}')).Text -split "`n"
    $nativeAbsent=@($containers|Where-Object {$_.Trim().StartsWith($prefix,[StringComparison]::Ordinal)}).Count -eq 0 -and
        @($networks|Where-Object {$_.Trim().StartsWith($prefix,[StringComparison]::Ordinal)}).Count -eq 0
    if(-not $nativeAbsent){throw 'PUBLIC_COLLECTION_NATIVE_ABSENCE_UNCONFIRMED'}
    # A real failed navigation must retain its bound cause with stdin still open.
    # It must neither publish an original nor disguise the engine failure as a barrier.
    $stage='failed-source-diagnostics'
    $failedRequest=[Guid]::NewGuid()
    $admission=Invoke-PublicProofSql "begin;set local role authenticated;select set_config('request.jwt.claim.sub','$user',true);select row_to_json(r) from hsr.create_public_scan('$org','$failedRequest','https://screenreader.privensic.com/assets/not-here.js','screenreader.privensic.com') r;commit;"
    $failedJob=($admission -split "`n"|Where-Object {$_ -match '^\{'})|ConvertFrom-Json
    if(-not $failedJob.job_id){throw 'PUBLIC_FAILED_SOURCE_ADMISSION_UNCONFIRMED'}
    $lines=@(& $DotNet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --once-public 2> (Join-Path $folder 'failed-worker.stderr.jsonl'))
    $failedExit=$LASTEXITCODE
    $lines|Set-Content (Join-Path $folder 'failed-worker.json') -Encoding utf8NoBOM
    $failedResult=($lines -join "`n")|ConvertFrom-Json
    $diagnostic=$failedResult.FailureDiagnostic
    if($failedExit -ne 3 -or $failedResult.State -cne 'Blocked' -or $failedResult.JobId -ne $failedJob.job_id -or
        $failedResult.ErrorCode -cne 'PUBLIC_WEBSITE_SOURCE_UNCONFIRMED' -or $diagnostic.Schema -cne 'hsr-public-reader-failure-v1' -or
        $diagnostic.SessionId -ne $failedJob.job_id -or $diagnostic.Sequence -ne 1 -or $diagnostic.Stage -cne 'Navigation' -or
        $diagnostic.Cause -cne 'TARGET_HTTP_ERROR' -or $diagnostic.HttpStatusCode -ne 404){throw 'PUBLIC_FAILED_SOURCE_DIAGNOSTIC_UNCONFIRMED'}
    $failureFacts=(Invoke-PublicProofSql "select json_build_object('State',state,'NoOriginal',result_text is null and result_digest is null and result_worker_id is null,'Leases',(select count(*) from hsr.public_scan_leases),'Queue',(select count(*) from pgmq.q_hsr_public_scan_jobs)) from hsr.public_scan_jobs where organization_id='$org' and id='$($failedJob.job_id)'")|ConvertFrom-Json
    if($failureFacts.State -cne 'blocked' -or -not $failureFacts.NoOriginal -or $failureFacts.Leases -ne 0 -or $failureFacts.Queue -ne 0){throw 'PUBLIC_FAILED_SOURCE_RETIREMENT_UNCONFIRMED'}
    $failedAck=@(Get-ChildItem -LiteralPath (Join-Path $journal 'removed') -Filter '*.json'|ForEach-Object {Get-Content $_.FullName -Raw|ConvertFrom-Json}|Where-Object {$_.Intent.SessionId -eq $failedJob.job_id})
    if($failedAck.Count -ne 1 -or -not $failedAck[0].ContainersAbsenceConfirmed -or -not $failedAck[0].NetworksAbsenceConfirmed -or
        @(Get-ChildItem -LiteralPath (Join-Path $journal 'intents')).Count -ne 0){throw 'PUBLIC_FAILED_SOURCE_OWNERSHIP_UNCONFIRMED'}
    $prefix='hsr-public-'+([Guid]$failedAck[0].Intent.Owner).ToString('N')+'-'
    $containers=(Invoke-ReaderRuntime $Runtime @('ps','--all','--format','{{.Names}}')).Text -split "`n"
    $networks=(Invoke-ReaderRuntime $Runtime @('network','ls','--format','{{.Name}}')).Text -split "`n"
    $nativeAbsent=$nativeAbsent -and @($containers|Where-Object {$_.Trim().StartsWith($prefix,[StringComparison]::Ordinal)}).Count -eq 0 -and
        @($networks|Where-Object {$_.Trim().StartsWith($prefix,[StringComparison]::Ordinal)}).Count -eq 0
    if(-not $nativeAbsent){throw 'PUBLIC_FAILED_SOURCE_NATIVE_ABSENCE_UNCONFIRMED'}
    $failureFacts|Add-Member -NotePropertyName Diagnostic -NotePropertyValue $diagnostic
    $ready=$true;$stage='completed'
}catch{
    $_.Exception.Message|Set-Content (Join-Path $folder 'failure.log') -Encoding utf8NoBOM
}finally{
    foreach($key in $prior.Keys){[Environment]::SetEnvironmentVariable($key,$prior[$key])}
    if($created){
        $item=((Invoke-ReaderRuntime $Runtime @('inspect',$name)).Text|ConvertFrom-Json)[0]
        if($item.Config.Labels.'hsr.public.collection' -cne $owner){throw 'PUBLIC_COLLECTION_DATABASE_OWNER_MISMATCH'}
        $null=Invoke-ReaderRuntime $Runtime @('rm','--force','--volumes',$item.Id)
        $remaining=(Invoke-ReaderRuntime $Runtime @('ps','--all','--no-trunc','--format','{{.ID}}')).Text -split "`n"
        $removed=@($remaining|Where-Object {$_.Trim() -ceq $item.Id}).Count -eq 0
    }
}
$result=[ordered]@{Schema='hsr-public-website-collection-proof-v1';Ready=($ready -and $removed -and $nativeAbsent);Stage=$stage;Run=$owner;Image=$Image;
    Facts=$facts;FailureFacts=$failureFacts;DatabaseRemoved=$removed;OwnedNativeAbsent=$nativeAbsent;HostedProjectChanged=$false;OriginalAccountAcceptance=$false;Folder=$folder}
$result|ConvertTo-Json -Depth 5|Tee-Object -FilePath (Join-Path $folder 'run.json')
if(-not $result.Ready){exit 3}
exit 0
