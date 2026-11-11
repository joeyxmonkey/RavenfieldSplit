[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RavenfieldDir,
    [string] $BepInExCore,
    [string] $Compiler,
    [string] $SdlRuntimeDir,
    [switch] $Package
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$RavenfieldDir = (Resolve-Path -LiteralPath $RavenfieldDir).Path
$managed = Join-Path $RavenfieldDir 'ravenfield_Data/Managed'
if (!$BepInExCore) { $BepInExCore = Join-Path $RavenfieldDir 'BepInEx/core' }
if (!$Compiler) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $Compiler = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
    }
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) {
    throw 'Install Visual Studio Build Tools with MSBuild and the C# compiler, or pass -Compiler. You can also build RavenfieldSplit.csproj with the .NET SDK and the .NET Framework 4.7.2 targeting pack.'
}
$references = @(
    (Join-Path $BepInExCore 'BepInEx.dll')
    (Join-Path $BepInExCore '0Harmony.dll')
)
foreach ($assembly in 'mscorlib', 'System', 'System.Core', 'netstandard', 'Assembly-CSharp',
    'Assembly-CSharp-firstpass', 'AstarPathfindingProject', 'UnityEngine', 'UnityEngine.CoreModule', 'UnityEngine.InputLegacyModule',
    'UnityEngine.PhysicsModule', 'UnityEngine.AnimationModule', 'UnityEngine.AudioModule', 'UnityEngine.IMGUIModule',
    'UnityEngine.UI', 'UnityEngine.UIModule', 'UnityEngine.TextRenderingModule') {
    $references += Join-Path $managed "$assembly.dll"
}
foreach ($reference in $references) {
    if (!(Test-Path -LiteralPath $reference -PathType Leaf)) { throw "Missing reference $reference" }
}
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$dll = Join-Path $out 'RavenfieldSplit.dll'
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/platform:x64',
    '/langversion:7.3', '/deterministic+', '/optimize+', "/out:$dll")
$arguments += $references | ForEach-Object { '/reference:' + (Resolve-Path -LiteralPath $_).Path }
$arguments += Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' |
    Select-Object -ExpandProperty FullName
& $Compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compilation exited with code $LASTEXITCODE." }
Write-Output $dll

if ($Package) {
    if (!$SdlRuntimeDir) { $SdlRuntimeDir = Join-Path $root '.tools/SDL2' }
    foreach ($file in 'SDL2.dll', 'LICENSE-SDL2.txt') {
        if (!(Test-Path -LiteralPath (Join-Path $SdlRuntimeDir $file) -PathType Leaf)) {
            throw "Missing $file in $SdlRuntimeDir. See the README for the official SDL2 download."
        }
    }
    $staging = Join-Path $root 'dist/staging'
    $pluginDir = Join-Path $staging 'BepInEx/plugins/RavenfieldSplit'
    $nativeDir = Join-Path $pluginDir 'native'
    $configDir = Join-Path $staging 'BepInEx/config'
    New-Item -ItemType Directory -Force -Path $nativeDir, $configDir | Out-Null
    Copy-Item -LiteralPath $dll -Destination $pluginDir
    Copy-Item -LiteralPath (Join-Path $SdlRuntimeDir 'SDL2.dll'), (Join-Path $SdlRuntimeDir 'LICENSE-SDL2.txt') -Destination $nativeDir
    Copy-Item -LiteralPath (Join-Path $root 'config/local.ravenfield.splitscreen.cfg') -Destination $configDir
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $staging
    $zip = Join-Path $out 'RavenfieldSplit-0.1.8.zip'
    Compress-Archive -LiteralPath (Join-Path $staging 'BepInEx'), (Join-Path $staging 'README.md') -DestinationPath $zip -Force
    Write-Output $zip
}
