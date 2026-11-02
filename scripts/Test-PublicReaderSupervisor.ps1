param([ValidateSet('podman','docker')][string]$Runtime='podman',[string]$DotNet='dotnet')
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$repoRoot=Split-Path $PSScriptRoot -Parent
$cli=Join-Path $repoRoot 'src/HeadlessScreenReader.Cli/bin/Release/net10.0/HeadlessScreenReader.Cli.dll'
$normalLines=& $DotNet $cli --check-public-reader-supervisor $Runtime $repoRoot
$normalExit=$LASTEXITCODE
$normal=($normalLines -join "`n")|ConvertFrom-Json
if($normalExit -ne 0 -or -not $normal.Ready){$normal|ConvertTo-Json -Compress;throw 'PUBLIC_READER_MANAGED_NORMAL_UNCONFIRMED'}
$image=[string]$normal.Image
$run=[Guid]::NewGuid().ToString('N')
$folder=Join-Path $repoRoot ('artifacts/public-reader-recovery/'+$run)
$journal=Join-Path $folder '.public-reader-runtime'
$foreignOwner=[Guid]::NewGuid().ToString('N')
$foreignName='hsr-public-'+$foreignOwner+'-private'
$foreignId=$null;$crash=$null;$own=$null;$unexpectedId=$null
try {
    $null=Invoke-ReaderRuntime $Runtime @('network','create','--label',"hsr.prototype.owner=$foreignOwner",$foreignName)
    $foreignId=$foreignName
    $created=((Invoke-ReaderRuntime $Runtime @('network','inspect',$foreignName)).Text|ConvertFrom-Json)[0]
    if($created.Labels.'hsr.prototype.owner' -cne $foreignOwner -or $created.Id -cnotmatch '^[a-f0-9]{64}$'){
        throw 'PUBLIC_READER_SENTINEL_CREATE_UNCONFIRMED'
    }
    $foreignId=[string]$created.Id
    $start=[Diagnostics.ProcessStartInfo]::new($DotNet)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($arg in @($cli,'--public-reader-supervisor-crash-child',$Runtime,$folder,$PSScriptRoot,$image)){$start.ArgumentList.Add($arg)}
    $crash=[Diagnostics.Process]::Start($start)
    $read=$crash.StandardOutput.ReadLineAsync()
    $errors=$crash.StandardError.ReadToEndAsync()
    if(-not $read.Wait([TimeSpan]::FromSeconds(90))){throw 'PUBLIC_READER_CRASH_CHILD_OPEN_UNCONFIRMED'}
    $opened=$read.GetAwaiter().GetResult()|ConvertFrom-Json
    if(-not $opened.Ready -or -not $opened.NativeObserved){throw 'PUBLIC_READER_CRASH_CHILD_OPEN_UNCONFIRMED'}
    $own=([Guid]$opened.Owner).ToString('N')
    $intentPath=Join-Path $journal ('intents/'+$own+'.json')
    $intent=Get-Content -LiteralPath $intentPath -Raw|ConvertFrom-Json
    if($intent.Owner -ne $opened.Owner -or $intent.Image -cne $image -or $intent.Runtime -cne $Runtime){throw 'PUBLIC_READER_CRASH_INTENT_UNCONFIRMED'}
    $prefix='hsr-public-'+$own
    $containerBefore=((Invoke-ReaderRuntime $Runtime @('inspect',"$prefix-worker","$prefix-proxy","$prefix-fence")).Text|ConvertFrom-Json)
    $networkBefore=((Invoke-ReaderRuntime $Runtime @('network','inspect',"$prefix-private","$prefix-egress")).Text|ConvertFrom-Json)
    if(@($containerBefore).Count -ne 3 -or @($networkBefore).Count -ne 2){throw 'PUBLIC_READER_CRASH_GROUP_UNCONFIRMED'}
    foreach($item in $containerBefore){
        $actual=[string]$item.Image;if($actual -cmatch '^[a-f0-9]{64}$'){$actual='sha256:'+$actual}
        if($item.Config.Labels.'hsr.prototype.owner' -cne $own -or $actual -cne $image){throw 'PUBLIC_READER_CRASH_GROUP_UNCONFIRMED'}
    }
    foreach($item in $networkBefore){if($item.Labels.'hsr.prototype.owner' -cne $own){throw 'PUBLIC_READER_CRASH_GROUP_UNCONFIRMED'}}
    $unexpectedName=$prefix+'-unexpected'
    $null=Invoke-ReaderRuntime $Runtime @('network','create','--label',"hsr.prototype.owner=$own",$unexpectedName)
    $unexpectedId=$unexpectedName
    $unexpected=((Invoke-ReaderRuntime $Runtime @('network','inspect',$unexpectedName)).Text|ConvertFrom-Json)[0]
    if($unexpected.Labels.'hsr.prototype.owner' -cne $own -or $unexpected.Id -cnotmatch '^[a-f0-9]{64}$'){
        throw 'PUBLIC_READER_UNEXPECTED_RESOURCE_CREATE_UNCONFIRMED'
    }
    $unexpectedId=[string]$unexpected.Id
    try {
        $null=& (Join-Path $PSScriptRoot 'Stop-IsolatedPublicReader.ps1') -Runtime $Runtime -OwnerToken $own -Image $image
        throw 'PUBLIC_READER_UNKNOWN_GROUP_WAS_NOT_DENIED'
    } catch {
        if($_.Exception.Message -cne 'PUBLIC_READER_RESOURCE_OWNERSHIP_INVALID'){throw}
    }
    $afterDeniedContainers=((Invoke-ReaderRuntime $Runtime @('inspect',"$prefix-worker","$prefix-proxy","$prefix-fence")).Text|ConvertFrom-Json)
    $afterDeniedNetworks=((Invoke-ReaderRuntime $Runtime @('network','inspect',"$prefix-private","$prefix-egress")).Text|ConvertFrom-Json)
    if((@($containerBefore.Id|Sort-Object) -join ',') -cne (@($afterDeniedContainers.Id|Sort-Object) -join ',') -or
        (@($networkBefore.Id|Sort-Object) -join ',') -cne (@($afterDeniedNetworks.Id|Sort-Object) -join ',') -or
        -not (Test-Path -LiteralPath $intentPath)) {throw 'PUBLIC_READER_DENIED_GROUP_WAS_CHANGED'}
    $null=Invoke-ReaderRuntime $Runtime @('network','rm',$unexpectedId)
    $unexpectedId=$null
    # Kill only the trusted .NET parent. Its finally block cannot acknowledge cleanup.
    $crash.Kill($false)
    if(-not $crash.WaitForExit(10000)){throw 'PUBLIC_READER_PARENT_EXIT_UNCONFIRMED'}
    if(-not $errors.Wait(10000) -or $errors.GetAwaiter().GetResult().Length -gt 16384){throw 'PUBLIC_READER_PARENT_DIAGNOSTICS_UNCONFIRMED'}
    if(-not (Test-Path -LiteralPath $intentPath)){throw 'PUBLIC_READER_CRASH_INTENT_LOST'}
    $recovery=$null
    for($attempt=0;$attempt -lt 3;$attempt++){
        $lines=& $DotNet $cli --recover-public-reader-supervisor $Runtime $folder $PSScriptRoot $image
        $recoverExit=$LASTEXITCODE
        $recovery=($lines -join "`n")|ConvertFrom-Json
        if($recoverExit -eq 0 -and $recovery.Ready){break}
        # A launcher may already be finishing EOF cleanup. Retry only while the
        # exact durable intent still exists; a lost result is not an acknowledgement.
        if(-not (Test-Path -LiteralPath $intentPath)){throw 'PUBLIC_READER_RECOVERY_RECEIPT_UNCONFIRMED'}
        Start-Sleep -Milliseconds 500
    }
    if($recoverExit -ne 0 -or -not $recovery.Ready -or $recovery.Recovered -ne 1){throw 'PUBLIC_READER_CRASH_RECOVERY_UNCONFIRMED'}
    $removed=Get-Content -LiteralPath (Join-Path $journal ('removed/'+$own+'.json')) -Raw|ConvertFrom-Json
    if(-not $removed.ContainersAbsenceConfirmed -or -not $removed.NetworksAbsenceConfirmed -or -not $removed.RecoveredAfterRestart -or
        $removed.Intent.Owner -ne $opened.Owner -or (Test-Path -LiteralPath $intentPath)){throw 'PUBLIC_READER_RECOVERY_ACK_UNCONFIRMED'}
    $containers=(Invoke-ReaderRuntime $Runtime @('ps','--all','--format','{{.Names}}')).Text -split "`n"
    $networks=(Invoke-ReaderRuntime $Runtime @('network','ls','--format','{{.Name}}')).Text -split "`n"
    if(@($containers|Where-Object {$_.Trim().StartsWith($prefix+'-',[StringComparison]::Ordinal)}).Count -ne 0 -or
        @($networks|Where-Object {$_.Trim().StartsWith($prefix+'-',[StringComparison]::Ordinal)}).Count -ne 0){throw 'PUBLIC_READER_RECOVERY_RESOURCES_REMAIN'}
    $foreign=((Invoke-ReaderRuntime $Runtime @('network','inspect',$foreignId)).Text|ConvertFrom-Json)[0]
    if($foreign.Labels.'hsr.prototype.owner' -cne $foreignOwner -or $foreign.Name -cne $foreignName){throw 'PUBLIC_READER_FOREIGN_OWNER_DAMAGED'}
    $null=Invoke-ReaderRuntime $Runtime @('network','rm',$foreignId)
    $foreignId=$null
    [ordered]@{Schema='hsr-public-reader-supervisor-acceptance-v1';Ready=$true;Run=$run;Normal=$normal;
        CrashParentKilled=$true;BeforeCrashContainers=3;BeforeCrashNetworks=2;Recovered=1;PendingIntents=0;
        ContainersAbsent=$true;NetworksAbsent=$true;UnrelatedOwnerPreserved=$true;SentinelRemoved=$true;
        UnexpectedResourceDeniedBeforeDeletion=$true;AllFiveOriginalIdsPreservedAfterDenial=$true;
        RecoveryNativeShutdownInferred=$false;HostedAdmissionInferred=$false}|ConvertTo-Json -Depth 6
} finally {
    if($crash){
        if(-not $crash.HasExited){$crash.Kill($true);$null=$crash.WaitForExit(10000)}
        $crash.Dispose()
    }
    if($own){
        if($unexpectedId){
            $unknown=((Invoke-ReaderRuntime $Runtime @('network','inspect',$unexpectedId)).Text|ConvertFrom-Json)[0]
            if($unknown.Labels.'hsr.prototype.owner' -ceq $own -and $unknown.Name -ceq $unexpectedName){
                $null=Invoke-ReaderRuntime $Runtime @('network','rm',$unexpectedId)
            }
        }
        # On failure, attempt only the exact journal-bound group. Keep its intent
        # when this emergency cleanup cannot produce the normal recovery receipt.
        try {$null=& (Join-Path $PSScriptRoot 'Stop-IsolatedPublicReader.ps1') -Runtime $Runtime -OwnerToken $own -Image $image} catch {Write-Warning 'PUBLIC_READER_EMERGENCY_REMOVAL_UNCONFIRMED'}
    }
    if($foreignId){
        $check=((Invoke-ReaderRuntime $Runtime @('network','inspect',$foreignId)).Text|ConvertFrom-Json)[0]
        if($check.Labels.'hsr.prototype.owner' -ceq $foreignOwner -and $check.Name -ceq $foreignName){$null=Invoke-ReaderRuntime $Runtime @('network','rm',$foreignId)}
    }
}
