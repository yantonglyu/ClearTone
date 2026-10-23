param([string]$Bundle=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$Bundle=[IO.Path]::GetFullPath($Bundle)
if(!(Test-Path -LiteralPath (Join-Path $Bundle 'app\ClearTone.exe'))){throw 'Preview bundle missing'}
$env:CLEARTONE_HOME=$Bundle
$env:CLEARTONE_DATA=Join-Path $Bundle 'preview-data'
$env:CLEARTONE_PORT='18779'
$env:CLEARTONE_PREVIEW='1'
if(!(Test-Path -LiteralPath $env:CLEARTONE_DATA)){
    New-Item -ItemType Directory -Path $env:CLEARTONE_DATA | Out-Null
    # Copy only settings and the account vault. Never replay the real listening outbox.
    foreach($name in @('state.json','account.dpapi','music-library.json')){
        $source=Join-Path 'E:\ClearTone\data' $name
        if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $env:CLEARTONE_DATA}
    }
}
Start-Process -FilePath (Join-Path $Bundle 'app\ClearTone.exe')
