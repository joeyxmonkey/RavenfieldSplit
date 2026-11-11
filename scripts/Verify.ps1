[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $RavenfieldDir,
    [Parameter(Mandatory)][string] $BepInExCore
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$managed = Join-Path (Resolve-Path -LiteralPath $RavenfieldDir).Path 'ravenfield_Data/Managed'
$core = (Resolve-Path -LiteralPath $BepInExCore).Path
[void][Reflection.Assembly]::LoadFrom((Join-Path $core 'Mono.Cecil.dll'))

$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$resolver.AddSearchDirectory($core)
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$game = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $managed 'Assembly-CSharp.dll'), $parameters)
$dll = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $root 'dist/RavenfieldSplit.dll'), $parameters)
try {
    $fingerprint = '15ab02d8-2bbf-4ac1-90d1-bef784c37587'
    if ($game.Mvid -ne [Guid] $fingerprint) { throw "Unexpected game assembly MVID $($game.Mvid)." }
    if ($dll.Architecture.ToString() -ne 'AMD64') { throw 'Expected an x64 plugin.' }
    $plugin = $dll.GetType('RavenfieldSplit.Plugin')
    $entry = @($plugin.CustomAttributes | Where-Object AttributeType -Match 'BepInPlugin')
    if ($entry.Count -ne 1 -or $entry[0].ConstructorArguments[0].Value -ne 'local.ravenfield.splitscreen') {
        throw 'BepInEx plugin entry point is missing.'
    }

    $patchSource = Get-Content -Raw -LiteralPath (Join-Path $root 'src/Patches.cs')
    $targets = @([regex]::Matches($patchSource, 'Patch\(typeof\((\w+)\), "(\w+)"') |
        ForEach-Object { "$($_.Groups[1].Value)::$($_.Groups[2].Value)" })
    $routed = [regex]::Match($patchSource, '(?s)foreach \(string method in new\[\]\s*\{([^}]+)\}')
    $targets += [regex]::Matches($routed.Groups[1].Value, '"(\w+)"') |
        ForEach-Object { "AiActorController::$($_.Groups[1].Value)" }
    if ($targets.Count -lt 40) { throw 'Patch targets could not be collected.' }
    foreach ($target in $targets) {
        $typeName, $methodName = $target -split '::'
        $methods = @($game.GetType($typeName).Methods | Where-Object Name -EQ $methodName)
        if ($methods.Count -ne 1 -or !$methods[0].HasBody) { throw "Invalid Harmony target $target." }
    }
    $fieldChecks = @{
        'Actor::SpawnWeapon' = 4
        'Projectile::StartTravelling' = 1
    }
    foreach ($target in $fieldChecks.Keys) {
        $typeName, $methodName = $target -split '::'
        $method = $game.GetType($typeName).Methods | Where-Object Name -EQ $methodName
        $branches = @($method.Body.Instructions | Where-Object {
            $_.OpCode.Name -eq 'ldfld' -and $_.Operand.FullName -eq 'System.Boolean Actor::aiControlled'
        })
        if ($branches.Count -ne $fieldChecks[$target]) { throw "Unexpected $target IL with $($branches.Count) AI checks." }
    }
    $patchedCalls = @{
        'Weapon::Equip' = 'System.Boolean Weapon::UserIsPlayer()'
        'Weapon::Update' = 'System.Boolean Weapon::UserIsPlayer()'
        'Weapon::SpawnProjectile' = 'UnityEngine.Transform ActorController::WeaponParent()'
        'Projectile::ProjectileRaycast' = 'UnityEngine.Collider UnityEngine.RaycastHit::get_collider()'
        'TargetTracker::TrackerIsEnabled' = 'System.Boolean Weapon::UserIsPlayer()'
        'TargetTracker::CanSeeVehicle' = 'System.Boolean Weapon::UserIsAI()'
        'TargetTracker::Update' = 'System.Boolean Weapon::UserIsAI()'
    }
    foreach ($target in $patchedCalls.Keys) {
        $typeName, $methodName = $target -split '::'
        $method = $game.GetType($typeName).Methods | Where-Object Name -EQ $methodName
        $calls = @($method.Body.Instructions | Where-Object {
            $_.OpCode.Name -in 'call', 'callvirt' -and $_.Operand.FullName -eq $patchedCalls[$target]
        })
        if ($calls.Count -ne 1) { throw "Unexpected call pattern in $target." }
    }

    $resolved = 0
    foreach ($type in $dll.GetTypes()) {
        foreach ($method in $type.Methods) {
            if (!$method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                $member = $instruction.Operand
                if ($member -isnot [Mono.Cecil.MethodReference] -and $member -isnot [Mono.Cecil.FieldReference]) { continue }
                if ($member.DeclaringType.Scope.Name -notmatch '^(Assembly-CSharp|AstarPathfindingProject|UnityEngine|BepInEx|0Harmony)') { continue }
                if ($null -eq $member.Resolve()) { throw "Unresolved reference $member in $method." }
                $resolved++
            }
        }
    }
    "Verified x64 BepInEx metadata, $($targets.Count) Harmony targets, nine weapon, projectile, and tracker IL patterns, and $resolved game/Unity/loader member references."
}
finally {
    $dll.Dispose()
    $game.Dispose()
    $resolver.Dispose()
}
