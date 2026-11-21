param(
    [Parameter(Mandatory=$true)][string]$Compiler,
    [string]$OutDir=(Join-Path $PSScriptRoot 'artifacts'),
    [string]$WindowsMetadata,
    [string]$UdkMetadata=(Join-Path $env:WINDIR 'SystemApps\MicrosoftWindows.UndockedDevKit_cw5n1h2txyewy\windowsudk.winmd'),
    [string]$TestSource,
    [string]$EntryPoint
)
$ErrorActionPreference='Stop'
if(!$WindowsMetadata){
    $sdk=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata'
    $WindowsMetadata=Get-ChildItem -LiteralPath $sdk -Filter Windows.winmd -Recurse |
        Where-Object {$_.Directory.Name -match '^10\.'} |
        Sort-Object { [version]$_.Directory.Name } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
foreach($file in @($Compiler,$WindowsMetadata,$UdkMetadata)){
    if(!$file -or !(Test-Path -LiteralPath $file)){throw "Required build file not found: $file"}
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$name=if($TestSource){[IO.Path]::GetFileNameWithoutExtension($TestSource)}else{'ClipPlus'}
$arguments=@('/nologo','/platform:x64','/optimize+','/nowarn:0649',
    ('/target:'+$(if($TestSource){'exe'}else{'winexe'})),('/out:'+(Join-Path $OutDir ($name+'.exe'))),
    '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',
    ('/r:'+$WindowsMetadata),('/r:'+$UdkMetadata))
foreach($reference in @('WPF\WindowsBase.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll',
    'System.Runtime.WindowsRuntime.dll','System.Runtime.dll','System.Runtime.InteropServices.WindowsRuntime.dll')){
    $arguments+=('/r:'+(Join-Path $framework $reference))
}
$arguments+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Source') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
if($TestSource){
    if(!$EntryPoint){throw 'Tests require -EntryPoint.'}
    $arguments+=@(('/main:'+$EntryPoint),$TestSource)
}
& $Compiler @arguments
if($LASTEXITCODE -ne 0){throw "Build failed: $name"}
Copy-Item -LiteralPath $UdkMetadata -Destination (Join-Path $OutDir 'windowsudk.winmd') -Force
if(!$TestSource){Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ClipPlusKeepAlive.vbs') -Destination $OutDir -Force}
