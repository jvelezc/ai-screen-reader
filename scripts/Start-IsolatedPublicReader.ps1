param([ValidateSet('podman','docker')][string]$Runtime='podman',
    [string]$AllowedHost='screenreader.privensic.com',
    [string]$TargetUrl='https://screenreader.privensic.com/ci/v1/source',
    [string]$OwnerToken, [string]$Image='localhost/headless-screen-reader-prototype:local', [switch]$NavigationTask,
    [string]$FailurePath)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$ctx=$null; $child=$null; $toWorker=$null; $nativeExit=$null; $failureStage='Isolation'
$exitCode=3
function Save-LaunchFailure($errorRecord,[string]$path,[string]$stage){
    if(-not $path){return}
    try {
        $cause=$errorRecord.Exception; while($cause.InnerException){$cause=$cause.InnerException}
        $category=if($cause -is [IO.IOException]){'Io'}elseif($cause -is [OperationCanceledException]){'Canceled'}else{'Runtime'}
        $record=[ordered]@{Schema='hsr-public-reader-launch-failure-v1';Owner=$OwnerToken;Stage=$stage;
            Category=$category;HResult=$cause.HResult;NativeExitCode=$nativeExit;
            InputTransferState=$(if($null -eq $toWorker){'NotStarted'}else{$toWorker.Status.ToString()})}
        $bytes=[Text.Encoding]::UTF8.GetBytes(($record | ConvertTo-Json -Compress))
        $file=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
        try{$file.Write($bytes)}finally{$file.Dispose()}
    } catch { # Missing diagnostics remain explicit; never send exception text into the binary control channel.
    }
}
try {
    $ctx=Start-ReaderIsolation -Runtime $Runtime -AllowedHosts $AllowedHost -OwnerToken $OwnerToken -Image $Image -PublicDependencies:$NavigationTask
    $failureStage='Launch'
    $mode=if($NavigationTask){'--public-task-worker'}else{'--public-reader-worker'}
    $workerArgs=(Get-ReaderWorkerArguments $ctx)+@('--entrypoint','dotnet','--interactive',$ctx.Image,
        '/app/cli/HeadlessScreenReader.Cli.dll',$mode,$ctx.ProxyIp,$AllowedHost,$ctx.Owner,$TargetUrl)
    # Copy raw OS byte streams; neither implicit handle inheritance on Windows
    # nor PowerShell's text pipeline preserves this binary protocol reliably.
    $start=[Diagnostics.ProcessStartInfo]::new($Runtime)
    $start.UseShellExecute=$false
    $start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true
    $start.RedirectStandardOutput=$true
    $start.RedirectStandardError=$true
    foreach($argument in $workerArgs){$start.ArgumentList.Add($argument)}
    $child=[Diagnostics.Process]::Start($start)
    if($null -eq $child){throw 'PUBLIC_READER_WORKER_START_UNCONFIRMED'}
    try {
        $failureStage='Transfer'
        $toWorker=[Console]::OpenStandardInput().CopyToAsync($child.StandardInput.BaseStream)
        $fromWorker=$child.StandardOutput.BaseStream.CopyToAsync([Console]::OpenStandardOutput())
        $errors=$child.StandardError.BaseStream.CopyToAsync([Console]::OpenStandardError())
        $inputClosed=$false
        while(-not $child.WaitForExit(50)){
            if($toWorker.IsCompleted -and -not $inputClosed){
                $null=$toWorker.GetAwaiter().GetResult()
                $child.StandardInput.Close()
                $inputClosed=$true
            }
        }
        # Successful Close must settle input. A failed reader cannot send that
        # acknowledgement: preserve its nonzero exit and drain its diagnostics
        # instead of replacing them with an unrelated input-transfer exception.
        $nativeExit=$child.ExitCode
        if($nativeExit -eq 0 -and -not $toWorker.Wait(2000)){throw 'PUBLIC_READER_INPUT_TRANSFER_UNCONFIRMED'}
        $null=$fromWorker.GetAwaiter().GetResult()
        $null=$errors.GetAwaiter().GetResult()
        $exitCode=$nativeExit
    } finally {$child.Dispose()}
    $failureStage='Cleanup'
    Stop-ReaderIsolation $ctx
    $ctx=$null
} catch {
    Save-LaunchFailure $_ $FailurePath $failureStage
    $exitCode=3
} finally {
    if($null -ne $ctx){
        try{Stop-ReaderIsolation $ctx}catch{
            $exitCode=3
            Save-LaunchFailure $_ $(if($FailurePath){$FailurePath+'.cleanup.json'}else{$null}) 'Cleanup'
        }
    }
}
exit $exitCode
