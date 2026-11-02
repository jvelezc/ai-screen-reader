param()
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$runtime=(Get-Process -Id $PID).Path
$cases=[Collections.Generic.List[string]]::new()
function Assert-StreamProof([bool]$condition,[string]$name){
    if(-not $condition){throw "RUNTIME_STREAM_CASE_UNCONFIRMED_$name"}
    $cases.Add($name)
}

$json='{"State":"Ready","Count":2}'
$warning='time="2026-11-02" level=warning msg="runtime diagnostic"'
$result=Invoke-ReaderRuntime $runtime @('-NoProfile','-Command',"[Console]::Error.WriteLine('$warning');[Console]::Out.Write('$json')")
Assert-StreamProof ($result.Code -eq 0 -and $result.Text -ceq $json -and
    ($result.Text|ConvertFrom-Json).Count -eq 2 -and $result.StandardError.Trim() -ceq $warning) 'SuccessfulRuntimeJsonExcludesDiagnosticStream'

$result=Invoke-ReaderRuntime $runtime @('-NoProfile','-Command','[Console]::Out.Write("partial");[Console]::Error.Write("native failure");exit 17') -AllowFailure
Assert-StreamProof ($result.Code -eq 17 -and $result.Text -ceq 'partial' -and
    $result.StandardError -ceq 'native failure') 'AllowedFailurePreservesSeparateOutputAndExitCode'

$failure=$null
try{$null=Invoke-ReaderRuntime $runtime @('-NoProfile','-Command','[Console]::Error.Write("native failure"+("x"*5000));exit 17')}
catch{$failure=$_.Exception.Message}
Assert-StreamProof ($null -ne $failure -and $failure.StartsWith('CONTAINER_COMMAND_FAILED (17): -NoProfile; native failure') -and
    $failure.Length -lt 1700) 'FailedRuntimeRetainsBoundedDiagnostic'

$result=Invoke-ReaderRuntime $runtime @('-NoProfile','-Command',"[Console]::Error.Write('$json')")
Assert-StreamProof ($result.Code -eq 0 -and $result.Text -ceq '' -and
    $result.StandardError -ceq $json) 'DiagnosticOnlyOutputCannotBecomeSuccessfulJson'

$unicode="café 'quoted' 日本語"
$payload=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($unicode))
$result=Invoke-ReaderRuntime $runtime @('-NoProfile','-Command',"[Console]::OutputEncoding=[Text.UTF8Encoding]::new(`$false);[Console]::Out.Write([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$payload')))")
Assert-StreamProof ($result.Code -eq 0 -and $result.Text -ceq $unicode -and
    $result.StandardError -ceq '') 'UnicodeOutputIsPreserved'

$result=Invoke-ReaderRuntime $runtime @('-NoProfile','-Command','[Console]::Out.Write("o"*131072);[Console]::Error.Write("e"*131072)')
Assert-StreamProof ($result.Code -eq 0 -and $result.Text -ceq ('o'*131072) -and
    $result.StandardError -ceq ('e'*131072)) 'BothRuntimePipesDrainBeyondTheirBuffer'

if($cases.Count -ne 6){throw 'RUNTIME_STREAM_CASE_COUNT_UNCONFIRMED'}
[pscustomobject]@{Schema='hsr-runtime-stream-proof-v1';State='Ready';Executed=6;Passed=$cases.Count;Failed=0;Cases=$cases}|ConvertTo-Json -Depth 3
