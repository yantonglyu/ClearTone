param([ValidateSet('Protect','Unprotect')][string]$Mode)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$inputBytes = [Convert]::FromBase64String([Console]::In.ReadToEnd().Trim())
$scope = [System.Security.Cryptography.DataProtectionScope]::CurrentUser
if ($Mode -eq 'Protect') { $outputBytes = [System.Security.Cryptography.ProtectedData]::Protect($inputBytes, $null, $scope) }
else { $outputBytes = [System.Security.Cryptography.ProtectedData]::Unprotect($inputBytes, $null, $scope) }
[Console]::Out.Write([Convert]::ToBase64String($outputBytes))
