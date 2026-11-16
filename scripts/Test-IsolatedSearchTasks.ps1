param([ValidateSet('podman','docker')][string]$Runtime='podman', [string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$searchOwner=[Guid]::NewGuid().ToString('N')
$searchImage="localhost/hsr-search-proof:$searchOwner"
$searchFolder=Join-Path $repoRoot "artifacts/search-task-runtime/$searchOwner"
New-Item -ItemType Directory -Path $searchFolder | Out-Null
$searchVariables=@('HSR_WORKER_RUNTIME','HSR_WORKER_IMAGE','HSR_WORKER_EVIDENCE','HSR_WORKER_SECCOMP')
$searchPrior=@{}; foreach($key in $searchVariables){$searchPrior[$key]=[Environment]::GetEnvironmentVariable($key)}
$searchBuilt=$false; $searchRemoved=$false; $searchPassed=$false; $searchStage='publish'; $searchError=$null
try {
    & $Dotnet publish (Join-Path $repoRoot 'src/HeadlessScreenReader.Cli/HeadlessScreenReader.Cli.csproj') -c Release -p:RestoreLockedMode=true -p:PlaywrightPlatform=linux-x64 -o (Join-Path $repoRoot 'artifacts/worker-build/cli') | Out-Null
    if($LASTEXITCODE -ne 0){throw 'SEARCH_TASK_FIXTURE_PUBLISH_FAILED'}
    $searchStage='image'
    $searchBuild=Invoke-ReaderRuntime $Runtime @('build','--tag',$searchImage,'--label',"hsr.search.proof=$searchOwner",'--file',(Join-Path $repoRoot 'ops/worker/Containerfile'),$repoRoot) -AllowFailure
    ($searchBuild.Text,$searchBuild.StandardError -join "`n") | Set-Content (Join-Path $searchFolder 'build.log') -Encoding utf8NoBOM
    if($searchBuild.Code -ne 0){throw 'SEARCH_TASK_IMAGE_BUILD_FAILED'}
    $searchBuilt=$true
    $searchStage='image-inspect'
    $searchImageId=(Invoke-ReaderRuntime $Runtime @('image','inspect',$searchImage,'--format','{{.Id}}')).Text.Trim()
    if($searchImageId -match '^[a-f0-9]{64}$'){$searchImageId='sha256:'+$searchImageId}
    if($searchImageId -notmatch '^sha256:[a-f0-9]{64}$'){throw 'SEARCH_TASK_IMAGE_ID_UNCONFIRMED'}
    $env:HSR_WORKER_RUNTIME=$Runtime; $env:HSR_WORKER_IMAGE=$searchImageId
    $env:HSR_WORKER_EVIDENCE=Join-Path $searchFolder 'state'
    $env:HSR_WORKER_SECCOMP=Join-Path $repoRoot 'ops/prototype/playwright-seccomp.json'
    $searchStage='native'
    $searchText=@(& $Dotnet (Join-Path $repoRoot 'artifacts/worker-build/cli/HeadlessScreenReader.Cli.dll') --check-isolated-search-tasks 2>&1)
    $searchExit=$LASTEXITCODE
    ($searchText -join "`n") | Set-Content (Join-Path $searchFolder 'native.json') -Encoding utf8NoBOM
    if($searchExit -ne 0){throw 'SEARCH_TASK_NATIVE_PROOF_UNCONFIRMED'}
    $searchReceipt=($searchText -join "`n") | ConvertFrom-Json
    if($searchReceipt.State -cne 'IsolatedSearchTasksVerified' -or $searchReceipt.NativeRemovals -ne 5 -or $searchReceipt.PendingNativeIntents -ne 0){throw 'SEARCH_TASK_NATIVE_RECEIPT_UNCONFIRMED'}
    $searchPassed=$true
} catch {
    # The native proof emits only closed stages/reasons; retain its receipt separately from this wrapper.
    $knownSearchErrors=@('SEARCH_TASK_FIXTURE_PUBLISH_FAILED','SEARCH_TASK_IMAGE_BUILD_FAILED','SEARCH_TASK_IMAGE_ID_UNCONFIRMED',
        'SEARCH_TASK_NATIVE_PROOF_UNCONFIRMED','SEARCH_TASK_NATIVE_RECEIPT_UNCONFIRMED')
    $searchError=if($_.Exception.Message -cin $knownSearchErrors){$_.Exception.Message}else{'SEARCH_TASK_WRAPPER_UNCONFIRMED'}
    $searchError | Set-Content (Join-Path $searchFolder 'error.txt') -Encoding utf8NoBOM
} finally {
    foreach($key in $searchVariables){[Environment]::SetEnvironmentVariable($key,$searchPrior[$key])}
    if($searchBuilt){
        $searchInspect=Invoke-ReaderRuntime $Runtime @('image','inspect',$searchImage) -AllowFailure
        if($searchInspect.Code -eq 0){
            $searchOwned=($searchInspect.Text | ConvertFrom-Json)[0]
            if($searchOwned.Config.Labels.'hsr.search.proof' -ceq $searchOwner){
                $searchUsers=Invoke-ReaderRuntime $Runtime @('ps','--all','--filter',"ancestor=$($searchOwned.Id)",'--format','{{.ID}}') -AllowFailure
                if($searchUsers.Code -eq 0 -and [string]::IsNullOrWhiteSpace($searchUsers.Text)){
                    $searchRemoval=Invoke-ReaderRuntime $Runtime @('rmi',$searchImage) -AllowFailure
                    $searchRemoved=$searchRemoval.Code -eq 0
                }
            }
        }
    }
}
@{Schema='hsr-search-task-runtime-wrapper-v1';Ready=$searchPassed;Stage=$searchStage;ErrorCode=$searchError;ImageRemoved=$searchRemoved;Folder=$searchFolder;Runtime=$Runtime} |
    ConvertTo-Json | Tee-Object -FilePath (Join-Path $searchFolder 'run.json')
if(-not $searchPassed -or -not $searchRemoved){exit 3}
