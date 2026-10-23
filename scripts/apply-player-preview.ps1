param([string]$Bundle=(Split-Path $PSScriptRoot -Parent),[string]$Install='E:\ClearTone',[switch]$CheckOnly)
$ErrorActionPreference='Stop'
$Bundle=[IO.Path]::GetFullPath($Bundle);$Install=[IO.Path]::GetFullPath($Install)
foreach($name in @('app\ClearTone.exe','app\ClearTone.dll','service\practice.cjs','analysis\analyze-song.py')){if(!(Test-Path -LiteralPath (Join-Path $Bundle $name))){throw "Missing bundle file: $name"}}
if(!(Test-Path -LiteralPath (Join-Path $Install 'data\state.json'))){throw 'Existing study records not found. Refusing an ambiguous upgrade.'}
if(@(Get-Process -Name ClearTone -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $Install 'app\ClearTone.exe') -or $_.Path -eq (Join-Path $Bundle 'app\ClearTone.exe') }).Count){throw '请先手动关闭正在使用的澄音和预览窗口，再运行更新。脚本不会关闭它们。'}
if($CheckOnly){Write-Output 'Ready to update. No files changed.';exit}
$backup=Join-Path $Install ('backups\before-player-preview-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath (Join-Path $Install 'app') -Destination (Join-Path $backup 'app') -Recurse
Copy-Item -LiteralPath (Join-Path $Install 'service') -Destination (Join-Path $backup 'service') -Recurse
if(Test-Path -LiteralPath (Join-Path $Install 'analysis')){Copy-Item -LiteralPath (Join-Path $Install 'analysis') -Destination (Join-Path $backup 'analysis') -Recurse}
New-Item -ItemType Directory -Path (Join-Path $backup 'data') | Out-Null
Get-ChildItem -LiteralPath (Join-Path $Install 'data') -File | Copy-Item -Destination (Join-Path $backup 'data')
foreach($folder in @('practice','practice-lyrics')){if(Test-Path -LiteralPath (Join-Path $Install ('data\'+$folder))){Copy-Item -LiteralPath (Join-Path $Install ('data\'+$folder)) -Destination (Join-Path $backup 'data') -Recurse}}
$state=Get-Content -LiteralPath (Join-Path $Install 'data\state.json') -Raw | ConvertFrom-Json
$hash=(Get-FileHash -LiteralPath (Join-Path $Install 'data\state.json')).Hash
Get-ChildItem -LiteralPath $backup -Recurse -File | ForEach-Object { [pscustomobject]@{Path=$_.FullName.Substring($backup.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash} } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'checksums.json') -Encoding utf8
Get-ChildItem -LiteralPath (Join-Path $Bundle 'app') | Copy-Item -Destination (Join-Path $Install 'app') -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $Bundle 'service') | Copy-Item -Destination (Join-Path $Install 'service') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $Bundle 'analysis') -Destination $Install -Recurse -Force
Copy-Item -LiteralPath (Join-Path $Bundle 'licenses') -Destination $Install -Recurse -Force
if((Get-FileHash -LiteralPath (Join-Path $Install 'data\state.json')).Hash -ne $hash){throw 'Study state unexpectedly changed. Restore from the backup before opening.'}
# Import practice corrections only. Never overwrite the latest study time/account with preview copies.
foreach($folder in @('practice','practice-lyrics')){
    $source=Join-Path $Bundle ('preview-data\'+$folder)
    if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $Install 'data') -Recurse -Force}
}
Remove-Item Env:CLEARTONE_HOME,Env:CLEARTONE_DATA,Env:CLEARTONE_PORT,Env:CLEARTONE_PREVIEW -ErrorAction SilentlyContinue
Write-Output "Updated; original learning records preserved. Backup: $backup"
# Let the existing desktop Explorer broker own the launch, outside terminal job lifetime.
Start-Process -FilePath explorer.exe -ArgumentList ('"'+(Join-Path $Install 'app\ClearTone.exe')+'"') -WindowStyle Hidden

