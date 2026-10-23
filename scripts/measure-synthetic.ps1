param([Parameter(Mandatory)][int]$ProcessId,[Parameter(Mandatory)][string]$Data,[string]$Scenario='sample',[int]$Samples=6,[int]$ServiceId=0)
$ErrorActionPreference='Stop'
$Data=[IO.Path]::GetFullPath($Data)
if(!(Test-Path -LiteralPath (Join-Path $Data '.cleartone-test'))){throw 'Synthetic test marker required'}
$target=Get-Process -Id $ProcessId
if($target.ProcessName -ne 'ClearTone'){throw 'Expected ClearTone test process'}
$ids=@($ProcessId)+@(Get-CimInstance Win32_Process | Where-Object ParentProcessId -eq $ProcessId | Select-Object -ExpandProperty ProcessId)
if($ServiceId -gt 0){$service=Get-Process -Id $ServiceId;if($service.ProcessName -ne 'node'){throw 'Expected service node process'};$ids=@($ids+$ServiceId|Select-Object -Unique)}
$logical=[Environment]::ProcessorCount
$previous=@{}
foreach($i in $ids){$p=Get-Process -Id $i -ErrorAction SilentlyContinue;if($p){$previous[$i]=$p.CPU}}
$watch=[Diagnostics.Stopwatch]::StartNew();$rows=@();$last=0
for($sample=0;$sample -lt $Samples;$sample++){
    Start-Sleep -Seconds 5
    $elapsed=$watch.Elapsed.TotalSeconds;$cpu=0;$private=0;$working=0
    foreach($i in $ids){$p=Get-Process -Id $i -ErrorAction SilentlyContinue;if($p){$cpu+=$p.CPU-$previous[$i];$previous[$i]=$p.CPU;$private+=$p.PrivateMemorySize64;$working+=$p.WorkingSet64}}
    $rows+=[pscustomobject]@{CPUPercent=[Math]::Round(100*$cpu/(($elapsed-$last)*$logical),3);PrivateMB=[Math]::Round($private/1MB,1);WorkingMB=[Math]::Round($working/1MB,1)};$last=$elapsed
}
$result=[pscustomobject]@{Scenario=$Scenario;LogicalProcessors=$logical;Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,1);Processes=$ids;Samples=$rows}
$result|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $Data ($Scenario+'-performance.json'))
$result|ConvertTo-Json -Depth 4 -Compress
