param(
    [ValidateSet('podman','docker')][string]$Runtime='podman',
    [Parameter(Mandatory)][ValidatePattern('^sha256:[a-f0-9]{64}$')][string]$Image,
    [Parameter(Mandatory)][string]$PublishedCli,
    [Parameter(Mandatory)][string]$Folder)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$owner=[Guid]::NewGuid().ToString('N');$name="hsr-s3-proof-$owner";$network="hsr-s3-net-$owner"
$folderPath=[IO.Path]::GetFullPath($Folder)
$artifactRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))+[IO.Path]::DirectorySeparatorChar
if(-not $folderPath.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $folderPath)){throw 'S3_PROOF_FOLDER_INVALID'}
New-Item -ItemType Directory -Path $folderPath|Out-Null
$server='docker.io/rustfs/rustfs@sha256:128220dc9382bac81940a1a9616ee8c274a3f3b8f647eedbeb50df5bb8b49922'
$created=$false;$networkCreated=$false;$removed=$false;$networkRemoved=$false;$ready=$false;$nativeRecovered=$false
$stage='server';$errorCode=$null;$proof=$null
$keys=@('HSR_WORKER_DATABASE','HSR_WORKER_IMAGE','HSR_WORKER_RUNTIME','HSR_WORKER_EVIDENCE','HSR_WORKER_SECCOMP',
    'HSR_S3_PROOF_IMAGE','HSR_S3_PROOF_RUNTIME','HSR_S3_PROOF_SECCOMP','HSR_S3_PROOF_EVIDENCE')
$prior=@{};foreach($key in $keys){$prior[$key]=[Environment]::GetEnvironmentVariable($key)}
try {
    $null=Invoke-ReaderRuntime $Runtime @('network','create','--internal','--label',"hsr.s3.proof=$owner",$network)
    $networkCreated=$true
    $null=Invoke-ReaderRuntime $Runtime @('create','--name',$name,'--network',$network,'--publish','127.0.0.1::9000',
        '--label',"hsr.s3.proof=$owner",'--env','RUSTFS_ACCESS_KEY=hsr-local-proof-access',
        '--env','RUSTFS_SECRET_KEY=hsr-local-proof-secret-synthetic-only',$server)
    $created=$true;$null=Invoke-ReaderRuntime $Runtime @('start',$name)
    $port=(Invoke-ReaderRuntime $Runtime @('port',$name,'9000/tcp')).Text.Trim()
    if($port -notmatch '^127\.0\.0\.1:([0-9]+)$'){throw 'S3_SERVER_LOOPBACK_UNCONFIRMED'}
    $portNumber=[int]$Matches[1]
    $clock=[Diagnostics.Stopwatch]::StartNew()
    do {
        try {$response=Invoke-WebRequest -Uri "http://127.0.0.1:$portNumber/" -NoProxy -TimeoutSec 2 -SkipHttpErrorCheck
            if([int]$response.StatusCode -in @(401,403)){break}}
        catch { }
        if($clock.Elapsed.TotalSeconds -gt 40){throw 'S3_SERVER_START_UNCONFIRMED'}
        Start-Sleep -Milliseconds 200
    }while($true)
    $stage='native-and-s3'
    $env:HSR_WORKER_DATABASE=$null
    $env:HSR_S3_PROOF_IMAGE=$Image;$env:HSR_S3_PROOF_RUNTIME=$Runtime
    $env:HSR_S3_PROOF_SECCOMP=Join-Path $repoRoot 'ops/prototype/playwright-seccomp.json'
    $env:HSR_S3_PROOF_EVIDENCE=Join-Path $folderPath 'evidence'
    $text=@(& dotnet (Join-Path $PublishedCli 'HeadlessScreenReader.Cli.dll') --check-s3-local "$portNumber" $owner 2>&1)
    $code=$LASTEXITCODE
    $text -join "`n"|Set-Content (Join-Path $folderPath 'protocol.log') -Encoding utf8NoBOM
    if($code -ne 0){throw 'S3_NATIVE_PROTOCOL_UNCONFIRMED'}
    $proof=($text -join "`n")|ConvertFrom-Json
    if($proof.Schema -ne 'hsr-local-s3-proof-v1' -or -not $proof.Ready -or $proof.Passed -ne 10 -or
        $proof.Cases.Count -ne 10 -or $proof.HostedProjectChanged -or $proof.AppJwtValidated -or
        $proof.DispatchedInputs -ne 0 -or $proof.GoalVerified){throw 'S3_NATIVE_RECEIPT_UNCONFIRMED'}
    $proof|ConvertTo-Json -Depth 4|Set-Content (Join-Path $folderPath 'protocol.json') -Encoding utf8NoBOM
    $ready=$true;$stage='completed'
} catch {$errorCode="S3_$($stage.ToUpperInvariant())_UNCONFIRMED"}
finally {
    if(Test-Path -LiteralPath (Join-Path $folderPath 'evidence/.runtime')){
        $env:HSR_WORKER_DATABASE=$null;$env:HSR_WORKER_IMAGE=$Image;$env:HSR_WORKER_RUNTIME=$Runtime
        $env:HSR_WORKER_EVIDENCE=Join-Path $folderPath 'evidence'
        $env:HSR_WORKER_SECCOMP=Join-Path $repoRoot 'ops/prototype/playwright-seccomp.json'
        $recovery=@(& dotnet (Join-Path $repoRoot 'src/HeadlessScreenReader.Worker/bin/Release/net10.0/HeadlessScreenReader.Worker.dll') --recover 2>&1)
        $recoveryCode=$LASTEXITCODE
        try {$nativeRecovered=$recoveryCode -eq 0 -and (($recovery -join "`n")|ConvertFrom-Json).State -eq 'Recovered'}catch{$nativeRecovered=$false}
    }
    if($created){
        $logs=Invoke-ReaderRuntime $Runtime @('logs',$name) -AllowFailure
        $logs.Text|Set-Content (Join-Path $folderPath 'server.log') -Encoding utf8NoBOM
        $item=((Invoke-ReaderRuntime $Runtime @('inspect',$name)).Text|ConvertFrom-Json)[0]
        if($item.Config.Labels.'hsr.s3.proof' -ne $owner){throw 'S3_SERVER_OWNER_MISMATCH'}
        $delete=Invoke-ReaderRuntime $Runtime @('rm','--force','--volumes',$name) -AllowFailure
        $remaining=Invoke-ReaderRuntime $Runtime @('inspect',$name) -AllowFailure
        $removed=$delete.Code -eq 0 -and $remaining.Code -ne 0
    }
    if($networkCreated){
        $item=((Invoke-ReaderRuntime $Runtime @('network','inspect',$network)).Text|ConvertFrom-Json)[0]
        if($item.Labels.'hsr.s3.proof' -ne $owner){throw 'S3_NETWORK_OWNER_MISMATCH'}
        $delete=Invoke-ReaderRuntime $Runtime @('network','rm',$network) -AllowFailure
        $remaining=Invoke-ReaderRuntime $Runtime @('network','inspect',$network) -AllowFailure
        $networkRemoved=$delete.Code -eq 0 -and $remaining.Code -ne 0
    }
    foreach($key in $keys){[Environment]::SetEnvironmentVariable($key,$prior[$key])}
}
$receipt=[pscustomobject]@{Schema='hsr-owned-s3-proof-v1';Ready=($ready -and $removed -and $networkRemoved -and $nativeRecovered);
    Stage=$stage;ErrorCode=$errorCode;Passed=if($proof){$proof.Passed}else{0};Cases=if($proof){$proof.Cases}else{@()};
    ServerRemoved=$removed;NetworkRemoved=$networkRemoved;NativeOwnershipRecovered=$nativeRecovered;HostedProjectChanged=$false}
$receipt|ConvertTo-Json -Depth 4|Set-Content (Join-Path $folderPath 'run.json') -Encoding utf8NoBOM
if(-not $receipt.Ready){throw 'OWNED_S3_ACCEPTANCE_UNCONFIRMED'}
$receipt
