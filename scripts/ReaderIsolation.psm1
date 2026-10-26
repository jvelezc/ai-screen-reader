$ErrorActionPreference = 'Stop'

function Resolve-ReaderDatabaseImage {
    param([ValidateSet('podman','docker')][string]$Runtime)
    # Both publisher registries serve this exact verified manifest, not merely the same tag.
    $digest='sha256:16c0944c77885446d4f2ebbb2191b357feeb3b1295bde2fb1978c113129dbeb4'
    $images=@("public.ecr.aws/supabase/postgres@$digest", "docker.io/supabase/postgres@$digest")
    foreach($image in $images){
        $cached=Invoke-ReaderRuntime $Runtime @('image','inspect',$image) -AllowFailure
        if($cached.Code -eq 0){return $image}
    }
    foreach($image in $images){
        $pull=Invoke-ReaderRuntime $Runtime @('pull',$image) -AllowFailure
        if($pull.Code -eq 0){
            $confirmed=Invoke-ReaderRuntime $Runtime @('image','inspect',$image) -AllowFailure
            if($confirmed.Code -eq 0){return $image}
        }
    }
    throw 'DATABASE_PINNED_IMAGE_ACQUISITION_UNCONFIRMED'
}

function Invoke-ReaderRuntime {
    param([string]$Runtime, [string[]]$Arguments, [switch]$AllowFailure)
    $lines = @(& $Runtime @Arguments 2>&1)
    $code = $LASTEXITCODE
    $text = ($lines | ForEach-Object { "$_" }) -join "`n"
    if ($code -ne 0 -and -not $AllowFailure) { throw "CONTAINER_COMMAND_FAILED ($code): $($Arguments[0]); $($text.Substring(0, [Math]::Min(1600, $text.Length)))" }
    return [pscustomobject]@{ Code = $code; Text = $text }
}

function Stop-ReaderIsolation {
    param($Context)
    foreach ($name in @($Context.Worker, $Context.Fence, $Context.Proxy)) {
        $read = Invoke-ReaderRuntime $Context.Runtime @('inspect', $name) -AllowFailure
        if ($read.Code -eq 0) {
            $item = ($read.Text | ConvertFrom-Json)[0]
            if ($item.Config.Labels.'hsr.prototype.owner' -ne $Context.Owner) { throw 'RESOURCE_OWNER_MISMATCH' }
            $null = Invoke-ReaderRuntime $Context.Runtime @('rm', '--force', $name)
        }
    }
    foreach ($name in @($Context.InternalNetwork, $Context.EgressNetwork)) {
        $read = Invoke-ReaderRuntime $Context.Runtime @('network', 'inspect', $name) -AllowFailure
        if ($read.Code -eq 0) {
            $item = ($read.Text | ConvertFrom-Json)[0]
            $labels = if ($item.Labels) { $item.Labels } else { $item.labels }
            if ($labels.'hsr.prototype.owner' -ne $Context.Owner) { throw 'RESOURCE_OWNER_MISMATCH' }
            $null = Invoke-ReaderRuntime $Context.Runtime @('network', 'rm', $name)
        }
    }
}

function Get-ReaderWorkerArguments {
    param($Context)
    $sandbox = @()
    if ($Context.Runtime -eq 'docker') {
        # Playwright's published default seccomp profile permits Chromium user namespaces.
        $sandbox = @('--security-opt', "seccomp=$(Join-Path (Split-Path $PSScriptRoot -Parent) 'ops/prototype/playwright-seccomp.json')")
    }
    return @('run', '--rm', '--name', $Context.Worker, '--network', "container:$($Context.Fence)",
        '--user', '1001:1001', '--cap-drop', 'ALL', '--cap-add', 'SYS_CHROOT', '--security-opt', 'no-new-privileges',
        '--read-only', '--memory', '1024m', '--cpus', '2', '--pids-limit', '256', '--shm-size', '256m',
        '--tmpfs', '/tmp:rw,nosuid,size=256m,mode=1777',
        '--tmpfs', '/home/pwuser:rw,nosuid,size=64m,mode=1777',
        '--label', "hsr.prototype.owner=$($Context.Owner)") + $Context.AcceptanceLabels + $sandbox
}

function Start-ReaderIsolation {
    param([ValidateSet('podman', 'docker')][string]$Runtime = 'podman', [string]$AllowedHosts = 'screenreader.privensic.com')
    $owner = [Guid]::NewGuid().ToString('N')
    $acceptanceLabels = @()
    if ($env:HSR_ACCEPTANCE_RUN_ID -match '^[0-9a-f]{32}$') {
        $acceptanceLabels = @('--label', "hsr.prototype.acceptance=$env:HSR_ACCEPTANCE_RUN_ID")
    }
    $prefix = 'hsr-proto-' + $owner
    $ctx = [pscustomobject]@{
        Owner = $owner; Runtime = $Runtime; Image = 'localhost/headless-screen-reader-prototype:local';
        InternalNetwork = "$prefix-private"; EgressNetwork = "$prefix-egress";
        Proxy = "$prefix-proxy"; Fence = "$prefix-fence"; Worker = "$prefix-worker";
        AllowedHosts = $AllowedHosts; ProxyIp = $null; Control = $null; KernelPolicies = $null; AcceptanceLabels = $acceptanceLabels
    }
    try {
        # Reap only unused networks created by this launcher after abrupt client termination.
        $old = Invoke-ReaderRuntime $Runtime @('network', 'ls', '--filter', 'label=hsr.prototype.owner', '--format', '{{.Name}}')
        foreach ($name in @($old.Text -split "`n" | Where-Object { $_ -match '^hsr-proto-[0-9a-f]{32}-(private|egress)$' })) {
            $network = ((Invoke-ReaderRuntime $Runtime @('network', 'inspect', $name)).Text | ConvertFrom-Json)[0]
            $networkOwner = $network.Labels.'hsr.prototype.owner'
            if ($networkOwner -notmatch '^[0-9a-f]{32}$' -or $name -notlike "hsr-proto-$networkOwner-*") { continue }
            $active = Invoke-ReaderRuntime $Runtime @('ps', '--all', '--filter', "label=hsr.prototype.owner=$networkOwner", '--format', '{{.ID}}')
            if ([string]::IsNullOrWhiteSpace($active.Text)) {
                $null = Invoke-ReaderRuntime $Runtime @('network', 'rm', $name) -AllowFailure
            }
        }
        $null = Invoke-ReaderRuntime $Runtime @('network', 'create', '--internal', '--label', "hsr.prototype.owner=$owner", $ctx.InternalNetwork)
        $null = Invoke-ReaderRuntime $Runtime @('network', 'create', '--label', "hsr.prototype.owner=$owner", $ctx.EgressNetwork)
        $null = Invoke-ReaderRuntime $Runtime (@('run', '--rm', '--detach') + $acceptanceLabels + @('--name', $ctx.Proxy, '--network', $ctx.EgressNetwork,
            '--user', '1001:1001', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', '--read-only',
            '--memory', '128m', '--cpus', '0.5', '--pids-limit', '64', '--tmpfs', '/tmp:rw,nosuid,size=32m',
            '--label', "hsr.prototype.owner=$owner", '--entrypoint', 'dotnet', $ctx.Image,
            '/app/egress/HeadlessScreenReader.Egress.dll', 'proxy', $AllowedHosts, $owner))
        $null = Invoke-ReaderRuntime $Runtime @('network', 'connect', $ctx.InternalNetwork, $ctx.Proxy)
        $inspect = ((Invoke-ReaderRuntime $Runtime @('inspect', $ctx.Proxy)).Text | ConvertFrom-Json)[0]
        $ctx.ProxyIp = $inspect.NetworkSettings.Networks.PSObject.Properties[$ctx.InternalNetwork].Value.IPAddress
        $parsedAddress = $null
        if (-not [Net.IPAddress]::TryParse($ctx.ProxyIp, [ref]$parsedAddress) -or $parsedAddress.AddressFamily -ne 'InterNetwork') { throw 'PROXY_ADDRESS_INVALID' }
        for ($attempt = 0; $attempt -lt 10; $attempt++) {
            $control = Invoke-ReaderRuntime $Runtime @('run', '--rm', '--network', $ctx.InternalNetwork, '--cap-drop', 'ALL',
                '--security-opt', 'no-new-privileges', '--read-only', '--user', '1001:1001', '--entrypoint', 'dotnet',
                $ctx.Image, '/app/egress/HeadlessScreenReader.Egress.dll', 'sentinel', $ctx.ProxyIp) -AllowFailure
            if ($control.Code -eq 0) { $ctx.Control = $control.Text | ConvertFrom-Json; break }
            Start-Sleep -Milliseconds 200
        }
        if (-not $ctx.Control.ControlTcpSentinel -or -not $ctx.Control.ControlUdpSentinel) { throw 'PRIVATE_SENTINEL_CONTROL_FAILED' }
        $null = Invoke-ReaderRuntime $Runtime (@('run', '--rm', '--detach') + $acceptanceLabels + @('--name', $ctx.Fence, '--network', $ctx.InternalNetwork,
            '--dns', '127.0.0.1', '--user', '0:0', '--cap-drop', 'ALL', '--cap-add', 'NET_ADMIN', '--security-opt', 'no-new-privileges',
            '--read-only', '--tmpfs', '/tmp:rw,nosuid,size=8m', '--tmpfs', '/run:rw,nosuid,size=8m',
            '--memory', '64m', '--pids-limit', '32', '--label', "hsr.prototype.owner=$owner", '--env', "HSR_PROXY_IP=$($ctx.ProxyIp)",
            '--entrypoint', '/bin/sh', $ctx.Image, '/app/firewall.sh'))
        $ready = $false
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            $check = Invoke-ReaderRuntime $Runtime @('exec', $ctx.Fence, 'test', '-f', '/tmp/fence-ready') -AllowFailure
            if ($check.Code -eq 0) { $ready = $true; break }
            Start-Sleep -Milliseconds 200
        }
        if (-not $ready) {
            $logs = Invoke-ReaderRuntime $Runtime @('logs', $ctx.Fence) -AllowFailure
            throw "NAMESPACE_FENCE_NOT_READY: $($logs.Text)"
        }
        $policies = @()
        foreach ($command in @('iptables', 'ip6tables')) {
            foreach ($chain in @('INPUT', 'OUTPUT', 'FORWARD')) {
                $rules = (Invoke-ReaderRuntime $Runtime @('exec', $ctx.Fence, $command, '-S', $chain)).Text
                if (-not $rules.Contains("-P $chain DROP")) { throw 'NAMESPACE_DEFAULT_DROP_MISSING' }
                $policies += $rules
            }
        }
        $ctx.KernelPolicies = $policies
        return $ctx
    } catch {
        Stop-ReaderIsolation $ctx
        throw
    }
}

Export-ModuleMember -Function Start-ReaderIsolation, Stop-ReaderIsolation, Get-ReaderWorkerArguments, Invoke-ReaderRuntime, Resolve-ReaderDatabaseImage
