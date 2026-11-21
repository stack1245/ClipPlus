param(
    [Parameter(Mandatory=$true)][string]$Compiler,
    [string]$OutDir=(Join-Path (Split-Path $PSScriptRoot) 'artifacts')
)
$ErrorActionPreference='Stop'
$build=Join-Path (Split-Path $PSScriptRoot) 'Build.ps1'
& $build -Compiler $Compiler -OutDir $OutDir
foreach($name in @('ActionRulesTests','GestureTests','ReleaseTriggerTests','ViewportResetTests',
    'DoubleDeleteTests','DeferredClearTests','IntegrationTests','BrokerTests')){
    & $build -Compiler $Compiler -OutDir $OutDir -TestSource (Join-Path $PSScriptRoot ($name+'.cs')) -EntryPoint $name
    & (Join-Path $OutDir ($name+'.exe')) (Join-Path $OutDir 'ClipPlus.exe')
    if($LASTEXITCODE -ne 0){throw "Test failed: $name"}
}
Write-Output 'All non-destructive tests passed.'
