param([Parameter(Mandatory=$true)][string]$Bundle)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$nugetRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget\packages'}
$lock=Get-Content -LiteralPath "$repo\src\ClearTone\packages.lock.json" -Raw | ConvertFrom-Json
$packages=($lock.dependencies.PSObject.Properties|Select-Object -First 1).Value
foreach($package in $packages.PSObject.Properties){
  $root=Join-Path $nugetRoot ($package.Name.ToLowerInvariant()+'\'+$package.Value.resolved)
  $notices=Get-ChildItem -LiteralPath $root -File | Where-Object Name -match '^(license|notice|third.party.notices)\.(txt|md)$'
  if($notices){
    $dest=Join-Path $Bundle ('licenses\nuget\'+$package.Name+'-'+$package.Value.resolved)
    New-Item -ItemType Directory -Path $dest -Force|Out-Null
    $notices|Copy-Item -Destination $dest -Force
  }
}
$sdk=Join-Path $nugetRoot ('microsoft.windowsappsdk.runtime\'+$packages.'Microsoft.WindowsAppSDK.Runtime'.resolved+'\license.txt')
$intro=@'
ClearTone 使用许可 / Component licenses

澄音自有源码使用 MIT 许可。随附的 Microsoft 运行库与第三方组件各自遵守其原始许可，应用的 MIT 许可不取代这些许可，也不授予音乐内容或平台账号的使用权。

安装目录中的 licenses 文件夹保留完整组件声明。以下 Microsoft Windows App SDK 条款仅适用于对应的 Microsoft 组件，不限制 MIT 或 LGPL 等独立开源组件授予的权利。

ClearTone source is MIT licensed. Bundled components retain their own terms, supplied under licenses/. The Microsoft terms below apply only to the corresponding Microsoft components and do not replace rights granted by the independent open-source licenses.

'@
[IO.File]::WriteAllText((Join-Path $Bundle 'INSTALL-LICENSE.txt'),($intro+(Get-Content -LiteralPath "$repo\LICENSE" -Raw)+"`r`n`r`n"+(Get-Content -LiteralPath $sdk -Raw)),[Text.UTF8Encoding]::new($true))
