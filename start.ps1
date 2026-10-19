param([switch]$SkipBuild, [switch]$Aspire)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
 if ($Aspire) {
  $dockerOs = docker info --format '{{.OSType}}'
  if ($LASTEXITCODE -ne 0 -or $dockerOs -ne 'linux') { throw 'Start Docker Desktop with Linux containers, then run ./start.ps1 -Aspire again.' }
  dotnet run --project src/Journey.AppHost
  if ($LASTEXITCODE -ne 0) { throw 'Aspire startup failed' }
  return
 }
 if (-not $SkipBuild) {
  Push-Location src/Journey.Client
  try { npm ci; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }; npm run build; if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' } } finally { Pop-Location }
 }
 dotnet run --project src/Journey.Server
} finally { Pop-Location }
