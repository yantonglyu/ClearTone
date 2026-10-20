param([string]$Output=(Join-Path $PSScriptRoot '..\artifacts\ClearTone'))
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$Output=[IO.Path]::GetFullPath($Output)
$node=(Get-Command node.exe -ErrorAction Stop).Source
$npm=(Get-Command npm.cmd -ErrorAction Stop).Source
Push-Location (Join-Path $repo 'service')
try { & $npm ci --ignore-scripts; if($LASTEXITCODE -ne 0){throw 'npm ci failed'} } finally {Pop-Location}
& dotnet publish (Join-Path $repo 'src\ClearTone\ClearTone.csproj') -c Release -p:Platform=x64 -p:RestoreLockedMode=true -r win-x64 --self-contained true -o (Join-Path $Output 'app')
if($LASTEXITCODE -ne 0){throw 'Native publish failed'}
New-Item -ItemType Directory -Path "$Output\service","$Output\runtime","$Output\analysis" -Force|Out-Null
# Explicit inputs only. Never package the developer's accounts or caches.
foreach($name in @('server.cjs','song-library.cjs','analysis-jobs.cjs','audio-format.cjs','dpapi.ps1','package.json','package-lock.json')){Copy-Item -LiteralPath "$repo\service\$name" -Destination "$Output\service" -Force}
Copy-Item -LiteralPath "$repo\service\node_modules" -Destination "$Output\service" -Recurse -Force
Get-ChildItem "$repo\analysis" -File|Copy-Item -Destination "$Output\analysis" -Force
Copy-Item -LiteralPath $node -Destination "$Output\runtime\node.exe" -Force
foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md','README.md')){Copy-Item -LiteralPath "$repo\$name" -Destination $Output -Force}
Copy-Item -LiteralPath "$repo\licenses" -Destination $Output -Recurse -Force
Copy-Item -LiteralPath "$PSScriptRoot\install.ps1","$PSScriptRoot\setup-analysis.ps1" -Destination $Output -Force
Write-Host "Ready: $Output\app\ClearTone.exe"
