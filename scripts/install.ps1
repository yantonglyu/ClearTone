param([string]$Destination=(Join-Path $env:LOCALAPPDATA 'Programs\ClearTone'),[string]$Bundle=$PSScriptRoot)
$ErrorActionPreference='Stop'
$Destination=[IO.Path]::GetFullPath($Destination)
if(!(Test-Path "$Bundle\app\ClearTone.exe")){throw 'Run this installer from a built bundle.'}
if(Test-Path $Destination){throw 'Choose a new installation directory. Existing installations and data are not overwritten.'}
New-Item -ItemType Directory -Path $Destination|Out-Null
foreach($name in @('app','service','runtime','analysis','licenses','LICENSE','THIRD_PARTY_NOTICES.md','README.md','setup-analysis.ps1')){Copy-Item -LiteralPath "$Bundle\$name" -Destination $Destination -Recurse}
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'ClearTone.lnk'))
$shortcut.TargetPath="$Destination\app\ClearTone.exe"
$shortcut.WorkingDirectory="$Destination\app"
$shortcut.IconLocation="$Destination\app\Assets\AppIcon.ico,0"
$shortcut.Save()
Write-Host "Installed to $Destination. User data: $env:LOCALAPPDATA\ClearTone"
