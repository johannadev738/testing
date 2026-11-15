param()
$ErrorActionPreference = 'Stop'
$voiceProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$voiceCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$voiceSpeechRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech'
$voiceSpeechAssembly = Get-ChildItem -LiteralPath $voiceSpeechRoot -Recurse -Filter 'System.Speech.dll' | Select-Object -First 1
if (-not (Test-Path -LiteralPath $voiceCompiler) -or $null -eq $voiceSpeechAssembly) {
    throw 'The installed Windows .NET Framework compiler and System.Speech assembly are required. Nothing was installed.'
}
$voiceOutputDirectory = Join-Path $voiceProjectRoot 'Assets\StreamingAssets\SimSafeVoice'
$null = New-Item -ItemType Directory -Force -Path $voiceOutputDirectory
$voiceOutput = Join-Path $voiceOutputDirectory 'WindowsVoiceHost.exe'
& $voiceCompiler /nologo /target:exe /optimize+ /platform:anycpu "/reference:$($voiceSpeechAssembly.FullName)" "/out:$voiceOutput" (Join-Path $PSScriptRoot 'WindowsVoiceHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Offline speech host compilation failed.' }
Write-Output 'Built local WindowsVoiceHost.exe. No microphone, cloud service or package installation was used.'
