param([Parameter(Mandatory=$true)][string]$App,[string]$Install='E:\ClearTone',[string]$Service,[switch]$NoLaunch)
$ErrorActionPreference='Stop'
$App=[IO.Path]::GetFullPath($App)
if(!(Test-Path -LiteralPath (Join-Path $App 'ClearTone.exe'))){throw 'Missing native app'}
if(!(Test-Path -LiteralPath (Join-Path $Install 'data\state.json'))){throw 'Existing profile required'}
if(Get-Process ClearTone -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $Install 'app\ClearTone.exe')){throw '请先手动关闭正在使用的澄音。更新不会关闭它。'}
if($Service -and !(Test-Path -LiteralPath (Join-Path $Service 'server.cjs'))){throw 'Missing source service'}
$backup=Join-Path $Install ('backups\before-native-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path (Join-Path $backup 'data') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $Install 'app') -Destination (Join-Path $backup 'app') -Recurse
Get-ChildItem -LiteralPath (Join-Path $Install 'data') -File|Copy-Item -Destination (Join-Path $backup 'data')
$eggs=Join-Path $Install 'data\easter-eggs'
if(Test-Path -LiteralPath $eggs){Copy-Item -LiteralPath $eggs -Destination (Join-Path $backup 'data') -Recurse}
if($Service){
    $serviceBackup=Join-Path $backup 'service'
    New-Item -ItemType Directory -Path $serviceBackup | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $Install 'service') -File | Copy-Item -Destination $serviceBackup
    $serverPath=Join-Path $Install 'service\server.cjs'
    Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.ExecutablePath -eq (Join-Path $Install 'runtime\node.exe') -and $_.CommandLine -and $_.CommandLine.Replace('/','\').Contains($serverPath,[StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { Stop-Process -Id $_.ProcessId }
}
$hashes=@{}
Get-ChildItem -LiteralPath (Join-Path $Install 'data') -File|ForEach-Object {$hashes[$_.Name]=(Get-FileHash -LiteralPath $_.FullName).Hash}
Get-ChildItem -LiteralPath $App|Copy-Item -Destination (Join-Path $Install 'app') -Recurse -Force
if($Service){Get-ChildItem -LiteralPath $Service -File | Where-Object Extension -in '.cjs','.ps1','.json' | Copy-Item -Destination (Join-Path $Install 'service') -Force}
foreach($name in $hashes.Keys){if((Get-FileHash -LiteralPath (Join-Path $Install ('data\'+$name))).Hash -ne $hashes[$name]){throw "Profile changed unexpectedly: $name"}}
$hashes|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $backup 'profile-checksums.json') -Encoding utf8
Remove-Item Env:CLEARTONE_HOME,Env:CLEARTONE_DATA,Env:CLEARTONE_PORT,Env:CLEARTONE_PREVIEW -ErrorAction SilentlyContinue
Write-Output "Native app updated. Profile unchanged. Backup: $backup"
if(!$NoLaunch){Start-Process explorer.exe -ArgumentList ('"'+(Join-Path $Install 'app\ClearTone.exe')+'"') -WindowStyle Hidden}


