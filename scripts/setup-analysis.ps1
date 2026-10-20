param([string]$Root=$PSScriptRoot,[string]$Python='python.exe')
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
if(!(Test-Path "$Root\service\server.cjs")){throw 'Point -Root to a built ClearTone bundle.'}
Write-Host 'Installing optional CPU analysis dependencies. This downloads PyTorch and models on first analysis.'
& $Python -m venv "$Root\analysis-runtime"
if($LASTEXITCODE -ne 0){throw 'Python 3.11-3.13 required'}
$py="$Root\analysis-runtime\Scripts\python.exe"
& $py -m pip install torch==2.11.0 torchaudio==2.11.0 --index-url https://download.pytorch.org/whl/cpu
if($LASTEXITCODE -ne 0){throw 'PyTorch installation failed'}
& $py -m pip install -r "$Root\analysis\requirements.txt"
if($LASTEXITCODE -ne 0){throw 'Analysis dependencies failed'}
New-Item -ItemType Directory -Path "$Root\analysis-models" -Force|Out-Null
[IO.File]::WriteAllText("$Root\service\analysis-config.json",(@{python=$py;models="$Root\analysis-models"}|ConvertTo-Json))
Write-Host 'Analysis enabled. Restart ClearTone. Models download during the first eligible song analysis.'
