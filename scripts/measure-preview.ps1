param([Parameter(Mandatory)][int]$ProcessId,[Parameter(Mandatory)][string]$Data,[int]$Samples=6)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath (Join-Path $Data '.cleartone-test'))){throw 'Synthetic test marker required'}
$root=Get-Process -Id $ProcessId
if($root.Path -notmatch '\\artifacts\\ClearTone-Player-0\.2-preview\\app\\ClearTone.exe$'){throw 'Only the isolated preview may be measured by this harness'}
$ids=@($ProcessId)+@(Get-CimInstance Win32_Process | Where-Object ParentProcessId -eq $ProcessId | Select-Object -ExpandProperty ProcessId)
$logical=(Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
$rows=@();$previous=@{};foreach($id in $ids){$p=Get-Process -Id $id -ErrorAction SilentlyContinue;if($p){$previous[$id]=$p.TotalProcessorTime.TotalSeconds}}
$watch=[Diagnostics.Stopwatch]::StartNew();$last=0
for($i=0;$i -lt $Samples;$i++){
    Start-Sleep -Seconds 5
    $elapsed=$watch.Elapsed.TotalSeconds;$cpu=0;$private=0;$working=0
    foreach($id in $ids){$p=Get-Process -Id $id -ErrorAction SilentlyContinue;if($p){$cpu+=$p.TotalProcessorTime.TotalSeconds-$previous[$id];$previous[$id]=$p.TotalProcessorTime.TotalSeconds;$private+=$p.PrivateMemorySize64;$working+=$p.WorkingSet64}}
    $rows+=[pscustomobject]@{Seconds=[Math]::Round($elapsed,2);CPUPercent=[Math]::Round(100*$cpu/(($elapsed-$last)*$logical),3);PrivateMB=[Math]::Round($private/1MB,1);WorkingMB=[Math]::Round($working/1MB,1)};$last=$elapsed
}
$rows | ConvertTo-Json | Tee-Object -FilePath (Join-Path $Data 'performance-sample.json')
