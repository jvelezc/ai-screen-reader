param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/prototype'),
    [switch]$SkipBrowserInstall,
    [switch]$InstallClaudeCode
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
$serverDirectory = Join-Path $output 'server'
New-Item -ItemType Directory -Force -Path $serverDirectory | Out-Null
dotnet publish (Join-Path $repoRoot 'src/HeadlessScreenReader.Mcp/HeadlessScreenReader.Mcp.csproj') -c Release -p:RestoreLockedMode=true -o $serverDirectory
if ($LASTEXITCODE -ne 0) { throw 'Reader publish failed.' }
if (-not $SkipBrowserInstall) {
    & pwsh -NoProfile -File (Join-Path $serverDirectory 'playwright.ps1') install chromium
    if ($LASTEXITCODE -ne 0) { throw 'Chromium installation failed.' }
}
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$assembly = Join-Path $serverDirectory 'HeadlessScreenReader.Mcp.dll'
$server = @{ command = $dotnet; args = @($assembly) }
$config = @{ mcpServers = @{ 'headless-reader' = $server } }
$configPath = Join-Path $output 'claude-mcp.json'
$config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM
if ($InstallClaudeCode) {
    Push-Location $repoRoot
    try {
        & claude mcp add --scope local --transport stdio headless-reader -- $dotnet $assembly
        if ($LASTEXITCODE -ne 0) { throw 'Claude Code registration failed; an existing entry was preserved. Inspect claude mcp get headless-reader.' }
    } finally { Pop-Location }
}
Write-Output "Prototype server: $assembly"
Write-Output "Claude Desktop/Code MCP configuration: $configPath"
Write-Output 'Start with: Use headless-reader to open the Foundation fixture, inspect it, and reach Details opened using actual keyboard focus. Verify the goal and show its timeline.'
