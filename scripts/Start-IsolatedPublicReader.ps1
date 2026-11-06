param([ValidateSet('podman','docker')][string]$Runtime='podman',
    [string]$AllowedHost='screenreader.privensic.com',
    [string]$TargetUrl='https://screenreader.privensic.com/ci/v1/source',
    [string]$OwnerToken, [string]$Image='localhost/headless-screen-reader-prototype:local')
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$ctx=Start-ReaderIsolation -Runtime $Runtime -AllowedHosts $AllowedHost -OwnerToken $OwnerToken -Image $Image
$exitCode=3
try {
    $workerArgs=(Get-ReaderWorkerArguments $ctx)+@('--entrypoint','dotnet','--interactive',$ctx.Image,
        '/app/cli/HeadlessScreenReader.Cli.dll','--public-reader-worker',$ctx.ProxyIp,$AllowedHost,$ctx.Owner,$TargetUrl)
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
        if($child.ExitCode -eq 0 -and -not $toWorker.Wait(2000)){throw 'PUBLIC_READER_INPUT_TRANSFER_UNCONFIRMED'}
        $null=$fromWorker.GetAwaiter().GetResult()
        $null=$errors.GetAwaiter().GetResult()
        $exitCode=$child.ExitCode
    } finally {$child.Dispose()}
} finally {
    Stop-ReaderIsolation $ctx
}
exit $exitCode
