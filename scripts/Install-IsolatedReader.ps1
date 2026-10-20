param(
    [ValidateSet('podman', 'docker')][string]$Runtime = 'podman',
    [string]$AllowedHosts = 'example.com,iana.org,www.iana.org',
    [switch]$SkipBuild,
    [switch]$InstallClaudeCode
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Build-IsolatedReader.ps1') -Runtime $Runtime
    if ($LASTEXITCODE -ne 0) { throw 'ISOLATED_BUILD_FAILED' }
}
$configPath = Join-Path $repoRoot 'artifacts/isolated/claude-mcp.json'
$server = @{
    command = (Get-Command pwsh -ErrorAction Stop).Source
    args = @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'Start-IsolatedReader.ps1'),
        '-Runtime', $Runtime, '-AllowedHosts', $AllowedHosts)
}
@{ mcpServers = @{ 'headless-reader-web' = $server } } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM
if ($InstallClaudeCode) {
    Push-Location $repoRoot
    try {
        $json = $server | ConvertTo-Json -Depth 4 -Compress
        & claude mcp add-json --scope local headless-reader-web $json
        if ($LASTEXITCODE -ne 0) { throw 'CLAUDE_WEB_REGISTRATION_FAILED; existing entries were preserved.' }
    } finally { Pop-Location }
}
Write-Output "Public reader configuration: $configPath"
Write-Output "Approved HTTPS hosts: $AllowedHosts"
