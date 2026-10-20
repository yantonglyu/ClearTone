param(
  [Parameter(Mandatory=$true)][string]$Bundle,
  [Parameter(Mandatory=$true)][string]$ISCC,
  [Parameter(Mandatory=$true)][string]$VCRuntime,
  [string]$ReleaseDir=(Join-Path $PSScriptRoot '..\artifacts\release'),
  [string]$Version='0.1.0'
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$Bundle=[IO.Path]::GetFullPath($Bundle)
$ReleaseDir=[IO.Path]::GetFullPath($ReleaseDir)
if(!(Test-Path -LiteralPath "$Bundle\app\ClearTone.exe")){throw 'Build a fresh bundle first'}
# App-local Microsoft redistributable CRT, never copy DLLs out of System32.
if(!(Test-Path -LiteralPath "$VCRuntime\msvcp140.dll")){throw 'Supply the x64 Microsoft.VC*.CRT folder from Visual Studio Redist'}
Get-ChildItem -LiteralPath $VCRuntime -Filter '*.dll' | ForEach-Object {
  if((Get-AuthenticodeSignature -LiteralPath $_.FullName).Status -ne 'Valid'){throw "Unsigned runtime: $($_.Name)"}
  Copy-Item -LiteralPath $_.FullName -Destination "$Bundle\app" -Force
  Copy-Item -LiteralPath $_.FullName -Destination "$Bundle\runtime" -Force
}
# Deny personal runtime state everywhere, including inside unexpected subfolders.
$forbidden=@('state.json','state.json.bak','state.json.tmp','account.dpapi','analysis-config.json','migration.json','control.json','diagnostics.json','errors.log','.env')
$files=Get-ChildItem -LiteralPath $Bundle -Recurse -File
foreach($file in $files){
  $relative=$file.FullName.Substring($Bundle.Length+1)
  $vendor=$relative.StartsWith('service\node_modules\')
  if($file.Name -in $forbidden -or $file.Name -like 'account.dpapi*' -or
    (!$vendor -and $relative -match '(^|\\)(data|smoke-data|analysis-cache|bili-audio|analysis-models|analysis-runtime|\.git)(\\|$)') -or
    $file.Extension -in @('.pdb','.wav','.mp3','.flac','.pth','.pt','.ckpt')){throw "Private or development file in bundle: $relative"}
}
& "$PSScriptRoot\installer-art.ps1"
New-Item -ItemType Directory -Path $ReleaseDir -Force|Out-Null
& $ISCC /Q "/DBundle=$Bundle" "/DReleaseDir=$ReleaseDir" "/DAppVersion=$Version" "$repo\installer\ClearTone.iss"
if($LASTEXITCODE -ne 0){throw 'Installer compiler failed'}
$exe=Join-Path $ReleaseDir "ClearTone-$Version-win-x64-Setup.exe"
$hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $ReleaseDir 'SHA256SUMS.txt'),"$hash  $([IO.Path]::GetFileName($exe))`n")
Write-Host "Release ready: $exe"
