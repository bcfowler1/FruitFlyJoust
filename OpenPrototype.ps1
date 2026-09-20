$ErrorActionPreference = 'Stop'
$editor = 'D:\Program Files (x86)\2022.3.62f3\Editor\Unity.exe'
if (!(Test-Path -LiteralPath $editor)) { throw 'Unity 2022.3.62f3 Editor was not found. Open this folder in Unity Hub.' }
Start-Process -FilePath $editor -ArgumentList @('-projectPath', ('"' + $PSScriptRoot + '"')) -WindowStyle Hidden
