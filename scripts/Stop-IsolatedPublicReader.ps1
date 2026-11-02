param([ValidateSet('podman','docker')][string]$Runtime='podman',
    [Parameter(Mandatory)][string]$OwnerToken,[Parameter(Mandatory)][string]$Image)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReaderIsolation.psm1') -Force -DisableNameChecking
$parsedOwner=[Guid]::Empty
if(-not [Guid]::TryParseExact($OwnerToken,'N',[ref]$parsedOwner) -or $parsedOwner -eq [Guid]::Empty -or
    $parsedOwner.ToString('N') -cne $OwnerToken -or $Image -cnotmatch '^sha256:[a-f0-9]{64}$'){
    throw 'PUBLIC_READER_OWNERSHIP_CONFIGURATION_INVALID'
}
$prefix='hsr-public-'+$OwnerToken
$expectedContainers=@("$prefix-worker","$prefix-fence","$prefix-proxy")
$expectedNetworks=@("$prefix-private","$prefix-egress")
function Read-OwnedNames([switch]$Networks){
    $arguments=if($Networks){@('network','ls')}else{@('ps','--all')}
    $format=if($Networks){'{{.Name}}'}else{'{{.Names}}'}
    $read=Invoke-ReaderRuntime $Runtime ($arguments+@('--format',$format))
    return @($read.Text -split "`n" | ForEach-Object {$_.Trim()} | Where-Object {$_.StartsWith($prefix+'-',[StringComparison]::Ordinal)})
}
# Validate the complete group before stopping any resource. Remove immutable IDs,
# so a replacement at the old name cannot become a different deletion target.
$containerIds=@();$networkIds=@()
foreach($name in @(Read-OwnedNames)){
    if($name -cnotin $expectedContainers){throw 'PUBLIC_READER_RESOURCE_OWNERSHIP_INVALID'}
    $item=((Invoke-ReaderRuntime $Runtime @('inspect',$name)).Text | ConvertFrom-Json)[0]
    $actualImage=[string]$item.Image
    if($actualImage -cmatch '^[a-f0-9]{64}$'){$actualImage='sha256:'+$actualImage}
    if($item.Config.Labels.'hsr.prototype.owner' -cne $OwnerToken -or $actualImage -cne $Image -or
        $item.Id -cnotmatch '^[a-f0-9]{64}$'){throw 'PUBLIC_READER_RESOURCE_OWNERSHIP_INVALID'}
    $containerIds+=@{Name=$name;Id=$item.Id}
}
foreach($name in @(Read-OwnedNames -Networks)){
    if($name -cnotin $expectedNetworks){throw 'PUBLIC_READER_RESOURCE_OWNERSHIP_INVALID'}
    $item=((Invoke-ReaderRuntime $Runtime @('network','inspect',$name)).Text | ConvertFrom-Json)[0]
    $labels=if($item.Labels){$item.Labels}else{$item.labels}
    $id=if($item.Id){[string]$item.Id}else{[string]$item.id}
    if($labels.'hsr.prototype.owner' -cne $OwnerToken -or $id -cnotmatch '^[a-f0-9]{64}$'){
        throw 'PUBLIC_READER_RESOURCE_OWNERSHIP_INVALID'
    }
    $networkIds+=@{Name=$name;Id=$id}
}
foreach($name in $expectedContainers){foreach($item in $containerIds){if($item.Name -ceq $name){$null=Invoke-ReaderRuntime $Runtime @('rm','--force',$item.Id)}}}
foreach($name in $expectedNetworks){foreach($item in $networkIds){if($item.Name -ceq $name){$null=Invoke-ReaderRuntime $Runtime @('network','rm',$item.Id)}}}
if(@(Read-OwnedNames).Count -ne 0 -or @(Read-OwnedNames -Networks).Count -ne 0){throw 'PUBLIC_READER_RESOURCE_ABSENCE_UNCONFIRMED'}
[ordered]@{Schema='hsr-public-reader-resource-removal-v1';Owner=$parsedOwner;Runtime=$Runtime;Image=$Image;
    ContainersAbsent=$true;NetworksAbsent=$true}|ConvertTo-Json -Compress
