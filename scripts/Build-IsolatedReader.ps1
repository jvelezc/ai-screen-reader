param([ValidateSet('podman', 'docker')][string]$Runtime = 'podman')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
dotnet publish (Join-Path $repoRoot 'src/HeadlessScreenReader.Mcp/HeadlessScreenReader.Mcp.csproj') -c Release -p:RestoreLockedMode=true -p:PlaywrightPlatform=linux-x64 -o (Join-Path $repoRoot 'artifacts/isolated/server')
if ($LASTEXITCODE -ne 0) { throw 'MCP_PUBLISH_FAILED' }
dotnet publish (Join-Path $repoRoot 'src/HeadlessScreenReader.Egress/HeadlessScreenReader.Egress.csproj') -c Release -p:RestoreLockedMode=true -o (Join-Path $repoRoot 'artifacts/isolated/egress')
if ($LASTEXITCODE -ne 0) { throw 'EGRESS_PUBLISH_FAILED' }
& $Runtime build --tag localhost/headless-screen-reader-prototype:local --file (Join-Path $repoRoot 'ops/prototype/Containerfile') $repoRoot
if ($LASTEXITCODE -ne 0) { throw 'ISOLATED_IMAGE_BUILD_FAILED' }
