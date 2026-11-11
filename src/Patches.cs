using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Bind = SteelInput.KeyBinds;

namespace RavenfieldSplit
{
    internal static class Patches
    {
        internal static readonly AccessTools.FieldRef<Actor, Vector3> MovementPosition =
            AccessTools.FieldRefAccess<Actor, Vector3>("movementPosition");
        internal static readonly AccessTools.FieldRef<Actor, bool> ForceAnimator =
            AccessTools.FieldRefAccess<Actor, bool>("forceAnimatorNeverCull");
        internal static readonly AccessTools.FieldRef<PlayerFpParent, Vector3> NativeWeaponOrigin =
            AccessTools.FieldRefAccess<PlayerFpParent, Vector3>("weaponParentOrigin");
        internal static readonly AccessTools.FieldRef<PlayerFpParent, Vector3> NativeShoulderOrigin =
            AccessTools.FieldRefAccess<PlayerFpParent, Vector3>("shoulderParentOrigin");
        internal static readonly AccessTools.FieldRef<PlayerFpParent, float> NativeHipFov =
            AccessTools.FieldRefAccess<PlayerFpParent, float>("normalFov");
        private static readonly AccessTools.FieldRef<Weapon, AudioClip[]> FireClips =
            AccessTools.FieldRefAccess<Weapon, AudioClip[]>("fireClips");
        private static readonly AccessTools.FieldRef<Weapon, bool> ShowsScopeObject =
            AccessTools.FieldRefAccess<Weapon, bool>("showScopeObject");
        private static readonly AccessTools.FieldRef<Weapon, List<Renderer>> NonScopeRenderers =
            AccessTools.FieldRefAccess<Weapon, List<Renderer>>("nonScopeRenderers");
        internal static readonly Action<LoadoutUi> RefreshNativeLoadout =
            AccessTools.MethodDelegate<Action<LoadoutUi>>(AccessTools.DeclaredMethod(typeof(LoadoutUi), "UpdateLoadout"));

        internal static void Install(Harmony harmony)
        {
            void Patch(Type type, string name, string prefix = null, string postfix = null, string transpiler = null)
            {
                MethodInfo target = AccessTools.DeclaredMethod(type, name) ??
                    throw new MissingMethodException(type.FullName, name);
                HarmonyMethod Hook(string hook) => hook == null ? null :
                    new HarmonyMethod(typeof(Patches), hook);
                harmony.Patch(target, Hook(prefix), Hook(postfix), Hook(transpiler));
            }

            Patch(typeof(SteelInput), "GetAxis", nameof(FirstAxis));
            Patch(typeof(SteelInput), "GetButton", nameof(FirstButton));
            Patch(typeof(SteelInput), "GetButtonDown", nameof(FirstButtonDown));
            Patch(typeof(SteelInput), "GetButtonUp", nameof(FirstButtonUp));
            Patch(typeof(SteelInput), "GetInput", postfix: nameof(FirstInputType));
            Patch(typeof(FpsActorController), "TogglePauseMenuInput", nameof(JoinInsteadOfPause));
            Patch(typeof(FpsActorController), "SelectedSpawnPoint", nameof(WaitForFirstLoadout));
            Patch(typeof(Actor), "SpawnAt", nameof(SelectTeammateSpawn));
            Patch(typeof(SquadLeaderKit), "Fire", nameof(OpenSquadOrders));
            Patch(typeof(TargetTracker), "TrackerIsEnabled", transpiler: nameof(TrackLocalPlayer));
            Patch(typeof(TargetTracker), "CanSeeVehicle", transpiler: nameof(UseLocalTrackingCone));
            Patch(typeof(TargetTracker), "Update", transpiler: nameof(UseLocalTrackingCone));
            Patch(typeof(Actor), "Update", nameof(UpdateActor));
            Patch(typeof(Actor), "UpdateMovement", postfix: nameof(CommitMovement));
            Patch(typeof(Actor), "IsReadyToSpawn", postfix: nameof(ReserveActor));
            Patch(typeof(Actor), "SpawnWeapon", postfix: nameof(SpawnedWeapon), transpiler: nameof(FirstPersonWeapons));
            Patch(typeof(Actor), "ShouldDistanceCull", postfix: nameof(DistanceCull));
            Patch(typeof(Actor), "EnterSeat", nameof(ProtectSeat));
            Patch(typeof(Actor), "Damage", nameof(BeforeDamage), nameof(AfterDamage));
            Patch(typeof(ActorManager), "GetNumberOfBotsInTeam", postfix: nameof(BotCount));
            Patch(typeof(ActorManager), "PlayerTakeOverBot", nameof(CanTakeOver));
            Patch(typeof(Weapon), "Equip", transpiler: nameof(EquipFirstPersonArms));
            Patch(typeof(Weapon), "Update", transpiler: nameof(UsePaneScopes));
            Patch(typeof(Weapon), "Shoot", postfix: nameof(AfterShot));
            Patch(typeof(Weapon), "UpdateSoundOutputGroup", nameof(UseWorldWeaponAudio));
            Patch(typeof(Weapon), "SpawnProjectile", nameof(AimCarriedProjectile), nameof(FinishCarriedProjectile),
                nameof(ProjectileWeaponParent));
            Patch(typeof(Projectile), "StartTravelling", transpiler: nameof(PlayerProjectileMask));
            Patch(typeof(Projectile), "OnKillCreditChange", postfix: nameof(ApplyProjectileOwnership));
            Patch(typeof(Projectile), "ProjectileRaycast", transpiler: nameof(FilterProjectileRaycast));
            Patch(typeof(Projectile), "Hit", nameof(NoteProjectileImpact));
            Patch(typeof(AiActorController), "Update", nameof(IsBot));
            Patch(typeof(AiActorController), "FixedUpdate", nameof(IsBot));
            Patch(typeof(AiActorController), "TickAiCoroutines", nameof(IsBot));
            Patch(typeof(AiActorController), "StartAiCoroutines", nameof(IsBot));
            Patch(typeof(AiActorController), "StartSeated", postfix: nameof(EnteredSeat));
            Patch(typeof(AiActorController), "SpawnAt", postfix: nameof(Spawned));
            Patch(typeof(AiActorController), "Die", postfix: nameof(Died));
            Patch(typeof(AiActorController), "FacingDirection", nameof(Facing));
            Patch(typeof(AiActorController), "Velocity", nameof(Velocity));
            Patch(typeof(AiActorController), "SwimInput", nameof(Swim));
            Patch(typeof(AiActorController), "WeaponParent", nameof(WeaponParent));
            Patch(typeof(AiActorController), "CarInput", nameof(GroundVehicle));
            Patch(typeof(AiActorController), "BoatInput", nameof(GroundVehicle));
            Patch(typeof(AiActorController), "AimInput", nameof(AimInput));
            Patch(typeof(AiActorController), "LadderInput", nameof(LadderInput));
            Patch(typeof(AiActorController), "AirplaneInput", nameof(Aircraft));
            Patch(typeof(AiActorController), "HelicopterInput", nameof(Aircraft));
            Patch(typeof(AiActorController), "OnWeaponMovement", postfix: nameof(UpdateModel));
            Patch(typeof(AiActorController), "EnableInput", postfix: nameof(InputEnabled));
            Patch(typeof(AiActorController), "DisableInput", postfix: nameof(InputEnabled));
            Patch(typeof(AiActorController), "EnableMovement", postfix: nameof(MovementEnabled));
            Patch(typeof(AiActorController), "DisableMovement", postfix: nameof(MovementEnabled));

            foreach (string method in new[]
            {
                "ProjectToGround", "UseEyeMuzzle", "UseMuzzleDirection", "WantsToFire", "Aiming", "Reload", "OnGround",
                "IsMoving", "IsSprinting", "UseSprintingAnimation", "HoldingSprint", "Jump", "Crouch",
                "Prone", "IsAlert", "IdlePose", "SwitchFireMode", "Countermeasures", "ForceStopVehicle"
            })
                Patch(typeof(AiActorController), method, nameof(ActorButton));
        }

        private static bool JoinInsteadOfPause(ref bool __result)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Plugin.Instance?.IsJoinPress() != true) return true;
            __result = false;
            return false;
        }

        private static PlayerOneInput First()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.RouteFirstInput) return null;
            plugin.SampleFirstInput();
            return plugin.FirstInput;
        }

        private static bool FirstAxis(Bind __0, ref float __result)
        {
            PlayerOneInput input = First();
            if (input == null) return true;
            __result = Plugin.Instance.SuppressFirstInput(__0) ? 0f : input.Axis(__0);
            return false;
        }

        private static bool FirstButton(Bind __0, ref bool __result)
        {
            PlayerOneInput input = First();
            if (input == null) return true;
            __result = !Plugin.Instance.SuppressFirstInput(__0) && input.Held(__0);
            return false;
        }

        private static bool FirstButtonDown(Bind __0, ref bool __result)
        {
            PlayerOneInput input = First();
            if (input == null) return true;
            __result = !Plugin.Instance.SuppressFirstInput(__0) && input.Down(__0);
            return false;
        }

        private static bool FirstButtonUp(Bind __0, ref bool __result)
        {
            PlayerOneInput input = First();
            if (input == null) return true;
            __result = !Plugin.Instance.SuppressFirstInput(__0) && input.Up(__0);
            return false;
        }

        private static void FirstInputType(ref SteelInput.Axis __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin != null && plugin.RouteFirstInput && plugin.FirstPad != null)
                __result.isMouseAxis = false;
        }

        private static bool WaitForFirstLoadout(FpsActorController __instance, ref SpawnPoint __result)
        {
            Plugin plugin = Plugin.Instance;
            if (__instance != LocalPlayer.controller || plugin?.RouteFirstInput != true) return true;
            DeploymentPicker deployment = plugin.FirstDeployment;
            if (deployment?.Active != true && plugin.FirstLoadout?.BlocksInput != true) return true;
            __result = deployment?.Confirmed == true && plugin.FirstLoadout?.BlocksInput != true &&
                __instance.allowAutoWaveRespawn ? deployment.Point : null;
            return false;
        }

        private static void SelectTeammateSpawn(Actor __instance, ref Vector3 __0, WeaponManager.LoadoutSet __2)
        {
            if (__instance != LocalPlayer.actor || __2 != null) return;
            Plugin plugin = Plugin.Instance;
            if (plugin?.RouteFirstInput != true) return;
            DeploymentPicker deployment = plugin.FirstDeployment;
            if (deployment != null && deployment.TryTeammatePosition(out Vector3 position)) __0 = position;
        }

        private static bool OpenSquadOrders(SquadLeaderKit __instance, ref bool __result)
        {
            Plugin plugin = Plugin.Instance;
            Actor user = __instance.user;
            if (plugin?.RouteFirstInput != true || user == null) return true;
            SecondPlayer second = Plugin.For(user);
            if (second != null) second.Orders.Open(second.World);
            else if (user == LocalPlayer.actor) plugin.FirstOrders?.Open(LocalPlayer.controller.GetActiveCamera());
            else return true;
            __result = false;
            return false;
        }

        private static bool IsBotWeaponUser(Weapon weapon) => weapon.UserIsAI() && Plugin.For(weapon.user) == null;

        private static IEnumerable<CodeInstruction> TrackLocalPlayer(IEnumerable<CodeInstruction> instructions) =>
            ReplaceSingleCall(instructions, AccessTools.Method(typeof(Weapon), nameof(Weapon.UserIsPlayer)),
                nameof(HasLocalWeaponUser));

        private static IEnumerable<CodeInstruction> UseLocalTrackingCone(IEnumerable<CodeInstruction> instructions) =>
            ReplaceSingleCall(instructions, AccessTools.Method(typeof(Weapon), nameof(Weapon.UserIsAI)),
                nameof(IsBotWeaponUser));

        private static bool IsBot(AiActorController __instance) => Plugin.For(__instance) == null;

        private static void UpdateActor(Actor __instance)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null || __instance.dead || __instance.isDeactivated) return;
            try { player.Tick(); }
            catch (Exception error) { Plugin.Instance.DisableForMatch(error); }
        }

        private static void CommitMovement(Actor __instance) => Plugin.For(__instance)?.CommitMovement();

        private static void ReserveActor(Actor __instance, ref bool __result)
        {
            if (Plugin.For(__instance) != null) __result = false;
        }

        private static void BotCount(int __0, ref int __result)
        {
            SecondPlayer player = Plugin.Instance?.Second;
            if (player != null && player.Actor != null && player.Actor.team == __0)
                __result = Mathf.Max(0, __result - 1);
        }

        private static bool CanTakeOver(Actor __0) => Plugin.For(__0) == null;

        private static bool ProtectSeat(Seat __0, ref bool __result)
        {
            if (__0 == null || Plugin.For(__0.occupant) == null) return true;
            __result = false;
            return false;
        }

        // Keep the actor AI-classified in game-wide systems, but run SpawnWeapon's native FPS branch.
        private static bool UseThirdPersonWeapon(Actor actor) => actor.aiControlled && Plugin.For(actor) == null;

        private static IEnumerable<CodeInstruction> FirstPersonWeapons(IEnumerable<CodeInstruction> instructions) =>
            ReplaceAiField(instructions, 4);

        private static void SpawnedWeapon(Actor __instance, Weapon __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (__result == null || player == null) return;
            Transform weapon = __result.transform;
            // This also covers call sites where Mono inlines AiActorController.WeaponParent.
            weapon.SetParent(player.WeaponRoot, false);
            weapon.localPosition = Vector3.zero;
            weapon.localRotation = Quaternion.identity;
            weapon.localScale = Vector3.one;
            __instance.SetImposterShadowMode(ShadowCastingMode.On);
            if (__result.animator != null) __result.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            __result.AssignFpAudioMix();
            AudioSource audio = __result.GetComponent<AudioSource>();
            if (audio != null && !__result.configuration.forceWorldAudioOutput) audio.spatialBlend = 0f;
        }

        private static void AfterShot(Weapon __instance, bool __result)
        {
            SecondPlayer player = Plugin.For(__instance.user);
            if (!__result || player == null || __instance.IsMountedWeapon()) return;
            AudioClip[] clips = __instance.playerFireSound?.clips;
            if (clips == null || clips.Length == 0) clips = FireClips(__instance);
            AudioClip clip = clips != null && clips.Length > 0 ? clips[UnityEngine.Random.Range(0, clips.Length)] : null;
            player.OnCarriedWeaponShot(__instance, clip);
        }

        private static void AimCarriedProjectile(Weapon __instance, ref Vector3 __0, ref Vector3 __1, out bool __state)
        {
            __state = false;
            SecondPlayer player = Plugin.For(__instance.user);
            if (player == null || __instance.IsMountedWeapon()) return;
            GameObject prefab = __instance.configuration.projectilePrefab;
            Projectile projectile = prefab != null ? prefab.GetComponent<Projectile>() : null;
            if (projectile == null || projectile.GetType() != typeof(Projectile)) return;
            Transform eye = player.World.transform;
            __0 = eye.forward;
            __1 = eye.position;
            __state = true;
        }

        private static void FinishCarriedProjectile(Projectile __result, bool __state)
        {
            if (__result == null) return;
            if (__state) __result.performInfantryInitialMuzzleTravel = false;
            Plugin.For(__result.killCredit)?.NoteProjectile(__result);
        }

        private static IEnumerable<CodeInstruction> FilterProjectileRaycast(IEnumerable<CodeInstruction> instructions) =>
            ReplaceSingleCall(instructions, AccessTools.PropertyGetter(typeof(RaycastHit), nameof(RaycastHit.collider)),
                nameof(GetProjectileCollider));

        private static Collider GetProjectileCollider(ref RaycastHit hit)
        {
            Collider collider = hit.collider;
            return Plugin.Instance?.Second?.IsMovementCapsule(collider) == true ? null : collider;
        }

        private static void NoteProjectileImpact(Projectile __instance, RaycastHit __1) =>
            Plugin.For(__instance.killCredit)?.NoteImpact(__1.collider);

        private static void BeforeDamage(Actor __instance, DamageInfo __0, out float __state)
        {
            Actor source = __0.sourceActor;
            __state = Plugin.For(source) != null && source.team != __instance.team ? __instance.health : float.NaN;
        }

        private static void AfterDamage(Actor __instance, DamageInfo __0, float __state)
        {
            float dealt = __state - Mathf.Max(0f, __instance.health);
            if (dealt > 0f) Plugin.For(__0.sourceActor)?.NoteDamage(dealt);
        }

        private static bool UseWorldWeaponAudio(Weapon __instance) =>
            __instance.IsMountedWeapon() || __instance.configuration.forceWorldAudioOutput ||
            Plugin.For(__instance.user) == null;

        private static bool HasLocalWeaponUser(Weapon weapon) =>
            weapon.UserIsPlayer() || Plugin.For(weapon.user) != null;

        internal static bool UsesEquipmentSight(Weapon weapon)
        {
            if (weapon == null || weapon.IsMountedWeapon()) return false;
            Weapon active = weapon.GetActiveSubWeapon();
            return weapon is SquadLeaderKit || weapon.targetTracker != null ||
                active is SquadLeaderKit || (active != null && active.targetTracker != null);
        }

        internal static bool IsAimingEquipment(Actor actor) =>
            actor != null && !actor.dead && !actor.fallenOver && actor.IsAiming() &&
            UsesEquipmentSight(actor.activeWeapon);

        private static IEnumerable<CodeInstruction> UsePaneScopes(IEnumerable<CodeInstruction> instructions) =>
            ReplaceSingleCall(instructions, AccessTools.Method(typeof(Weapon), nameof(Weapon.UserIsPlayer)),
                nameof(UseNativeScope));

        private static bool UseNativeScope(Weapon weapon)
        {
            if (!weapon.UserIsPlayer()) return false;
            Weapon held = weapon.user.activeWeapon;
            if (Plugin.Instance?.RouteFirstInput != true || weapon.user != LocalPlayer.actor ||
                !UsesEquipmentSight(held) || (weapon != held && weapon != held.GetActiveSubWeapon())) return true;
            GameObject scope = weapon.configuration.scopeAimObject;
            if (scope != null && scope.activeSelf) scope.SetActive(false);
            if (scope != null && ShowsScopeObject(weapon))
            {
                ShowsScopeObject(weapon) = false;
                List<Renderer> renderers = NonScopeRenderers(weapon);
                if (renderers != null)
                    foreach (Renderer renderer in renderers)
                        if (renderer != null) renderer.enabled = true;
            }
            return false;
        }

        private static Transform GetProjectileWeaponParent(ActorController controller) =>
            Plugin.For(controller.actor)?.WeaponRoot ?? controller.WeaponParent();

        private static IEnumerable<CodeInstruction> EquipFirstPersonArms(IEnumerable<CodeInstruction> instructions) =>
            ReplaceSingleCall(instructions, AccessTools.Method(typeof(Weapon), nameof(Weapon.UserIsPlayer)),
                nameof(HasLocalWeaponUser));

        private static IEnumerable<CodeInstruction> ProjectileWeaponParent(IEnumerable<CodeInstruction> instructions) =>
            ReplaceSingleCall(instructions, AccessTools.Method(typeof(ActorController), nameof(ActorController.WeaponParent)),
                nameof(GetProjectileWeaponParent));

        private static IEnumerable<CodeInstruction> ReplaceSingleCall(IEnumerable<CodeInstruction> instructions,
            MethodInfo original, string replacement)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Patches), replacement);
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException($"Expected one call to {original.Name}, found {replaced}.");
        }

        private static IEnumerable<CodeInstruction> PlayerProjectileMask(IEnumerable<CodeInstruction> instructions) =>
            ReplaceAiField(instructions, 1);

        private static IEnumerable<CodeInstruction> ReplaceAiField(IEnumerable<CodeInstruction> instructions, int expected)
        {
            FieldInfo field = AccessTools.Field(typeof(Actor), nameof(Actor.aiControlled));
            MethodInfo replacement = AccessTools.Method(typeof(Patches), nameof(UseThirdPersonWeapon));
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsField(field))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count != expected) throw new InvalidOperationException($"Expected {expected} actor checks, found {count}.");
        }

        private static void ApplyProjectileOwnership(Projectile __instance)
        {
            if (Plugin.For(__instance.killCredit) == null) return;
            __instance.firedByAI = false;
            __instance.firedByPlayer = true;
        }

        private static void DistanceCull(Actor __instance, ref bool __result)
        {
            SecondPlayer player = Plugin.Instance?.Second;
            if (player == null || !__result) return;
            if (ReferenceEquals(__instance, player.Actor) || player.VisibleFromSecondView(__instance.Position()))
                __result = false;
        }

        private static void Spawned(AiActorController __instance, Vector3 __0, Quaternion __1) =>
            Plugin.For(__instance)?.OnSpawn(__0, __1);

        private static void Died(AiActorController __instance) => Plugin.For(__instance)?.OnDeath();
        private static void EnteredSeat(AiActorController __instance, Seat __0) =>
            Plugin.For(__instance)?.OnSeatEntered(__0);

        private static void UpdateModel(AiActorController __instance) => Plugin.For(__instance)?.UpdatePose();

        private static void InputEnabled(AiActorController __instance, MethodBase __originalMethod)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player != null) player.InputEnabled = __originalMethod.Name == "EnableInput";
        }

        private static void MovementEnabled(AiActorController __instance, MethodBase __originalMethod)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player != null) player.MovementEnabled = __originalMethod.Name == "EnableMovement";
        }

        private static bool Facing(AiActorController __instance, ref Vector3 __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null) return true;
            __result = player.Facing;
            return false;
        }

        private static bool Velocity(AiActorController __instance, ref Vector3 __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null) return true;
            __result = player.Velocity;
            return false;
        }

        private static bool Swim(AiActorController __instance, ref Vector3 __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null) return true;
            __result = player.SwimDirection;
            return false;
        }

        private static bool WeaponParent(AiActorController __instance, ref Transform __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null) return true;
            __result = player.WeaponRoot;
            return false;
        }

        private static bool GroundVehicle(AiActorController __instance, ref Vector2 __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null) return true;
            __result = player.DriveInput;
            return false;
        }

        private static bool AimInput(AiActorController __instance, ref Vector2 __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null) return true;
            __result = player.LookInput;
            return false;
        }

        private static bool LadderInput(AiActorController __instance, ref float __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null) return true;
            __result = player.MoveInput.y;
            return false;
        }

        private static bool Aircraft(AiActorController __instance, ref Vector4 __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null) return true;
            __result = player.FlightInput;
            return false;
        }

        private static bool ActorButton(AiActorController __instance, MethodBase __originalMethod, ref bool __result)
        {
            SecondPlayer player = Plugin.For(__instance);
            if (player == null) return true;
            __result = player.ReadAction(__originalMethod.Name);
            return false;
        }
    }
}
