using System;
using UnityEngine;

namespace RavenfieldSplit
{
    internal sealed class SecondPlayer : IDisposable
    {
        private const int ViewModelLayer = 18;
        private const int TerrainAndVehicleMask = 2232321;
        private const float StandingHeight = 1.72f;
        private const float CrouchingHeight = 1.19f;
        private const float Radius = 0.27f;
        private readonly Plugin plugin;
        private readonly Pad pad;
        private readonly CharacterController capsule;
        private readonly Collider[] ownColliders;
        private readonly GameObject rig;
        private readonly Vector3 weaponOffset;
        private readonly Quaternion weaponRestRotation;
        private readonly float hipFov;
        private readonly AudioSource shotAudio;
        internal LoadoutPicker Loadout { get; }
        internal DeploymentPicker Deployment { get; }
        internal SquadOrders Orders { get; }
        private bool hasDeployed;
        private float yaw, pitch, recoilPitch, recoilYaw, verticalVelocity, walkPhase;
        private float aimBlend, weaponKick, weaponKickPitch, lastDamageTime = -1f;
        private int shotLogs, impactLogs, damageLogs;
        private float readyAt, previousSeatHeading;
        private float vehicleYaw;
        private int vehicleCameraFrame = -1;
        private bool chaseCamera;
        private float fallPeak;
        private Vector3 committedPosition;
        private bool wantsMove, grounded, crouch, sprint, enabledThisFrame;
        private Seat previousSeat;

        internal Actor Actor { get; }
        internal AiActorController Controller { get; }
        internal Transform WeaponRoot { get; }
        internal Camera World { get; }
        internal Camera ViewModel { get; }
        internal Vector3 Facing { get; private set; } = Vector3.forward;
        internal Vector3 Velocity { get; private set; }
        internal Vector3 SwimDirection { get; private set; }
        internal Vector2 MoveInput { get; private set; }
        internal Vector2 LookInput { get; private set; }
        internal Vector2 DriveInput { get; private set; }
        internal Vector4 FlightInput { get; private set; }
        internal bool InChaseCamera => chaseCamera && Actor.IsDriver() &&
            !Actor.seat.vehicle.IsAircraft() && !Actor.seat.vehicle.isTurret &&
            (!(Actor.activeWeapon is MountedWeapon) || !Actor.IsAiming());
        internal bool InputEnabled = true;
        internal bool MovementEnabled = true;
        internal bool HasController => pad.Connected;
        internal float RespawnRemaining => Mathf.Max(0f, readyAt - Time.time);
        internal bool ShowHitMarker => Time.time - lastDamageTime < 0.18f && lastDamageTime >= 0f;

        internal SecondPlayer(Plugin plugin, Actor actor, Pad pad)
        {
            this.plugin = plugin;
            this.pad = pad;
            Actor = actor;
            Controller = (AiActorController)actor.controller;
            ownColliders = actor.GetComponentsInChildren<Collider>(true);
            Loadout = new LoadoutPicker(SelectLoadout(), plugin.Settings.SaveSecondLoadout);
            Deployment = new DeploymentPicker(actor, LocalPlayer.actor);
            Orders = new SquadOrders(actor);
            rig = new GameObject("Ravenfield Split Player 2");
            shotAudio = rig.AddComponent<AudioSource>();
            shotAudio.playOnAwake = false;
            shotAudio.spatialBlend = 0f;
            shotAudio.priority = 0;
            shotAudio.outputAudioMixerGroup = GameManager.instance.fpMixerGroup;
            GameObject capsuleObject = new GameObject("Movement capsule");
            capsuleObject.layer = LocalPlayer.controller.gameObject.layer;
            capsuleObject.transform.SetParent(rig.transform);
            capsule = capsuleObject.AddComponent<CharacterController>();
            capsule.enabled = false;
            capsule.radius = Radius;
            capsule.skinWidth = 0.035f;
            capsule.stepOffset = 0.3f;
            capsule.slopeLimit = 50f;
            capsule.minMoveDistance = 0f;
            Resize(StandingHeight);

            FpsActorController local = LocalPlayer.controller;
            PlayerFpParent fp = local.fpParent;
            hipFov = Patches.NativeHipFov(fp);
            Matrix4x4 nativeWorldToLocal = NeutralMatrix(local.fpCamera.transform, fp).inverse;
            Transform nativeViewModel = local.fpViewModelCamera.transform;
            World = MakeCamera("Player 2 world", local.fpCamera);
            World.cullingMask &= ~((1 << ViewModelLayer) | (1 << 5) | (1 << 6));
            World.clearFlags = CameraClearFlags.Skybox;
            World.nearClipPlane = 0.05f;
            World.useOcclusionCulling = false;
            ViewModel = MakeCamera("Player 2 weapon", local.fpViewModelCamera);
            ViewModel.cullingMask = 1 << ViewModelLayer;
            ViewModel.clearFlags = CameraClearFlags.Depth;
            ViewModel.useOcclusionCulling = false;
            ViewModel.transform.SetParent(World.transform, false);
            Matrix4x4 viewPose = nativeWorldToLocal * NeutralMatrix(nativeViewModel, fp);
            ViewModel.transform.localPosition = viewPose.GetColumn(3);
            ViewModel.transform.localRotation = viewPose.rotation;
            WeaponRoot = new GameObject("First-person weapon root").transform;
            WeaponRoot.SetParent(World.transform, false);
            Matrix4x4 weaponPose = nativeWorldToLocal * NeutralMatrix(local.WeaponParent(), fp);
            weaponOffset = weaponPose.GetColumn(3);
            weaponRestRotation = weaponPose.rotation;
            WeaponRoot.localPosition = weaponOffset;
            WeaponRoot.localRotation = weaponRestRotation;
            readyAt = Time.time;
            actor.name = "Player 2";
            actor.isScheduledToSpawn = true;
        }

        private static Matrix4x4 NeutralMatrix(Transform transform, PlayerFpParent fp)
        {
            if (transform == null) return Matrix4x4.identity;
            Vector3 position = transform.localPosition;
            Quaternion rotation = transform.localRotation;
            if (transform == fp.weaponParent)
            {
                position = Patches.NativeWeaponOrigin(fp);
                rotation = Quaternion.identity;
            }
            else if (transform == fp.shoulderParent)
            {
                position = Patches.NativeShoulderOrigin(fp);
                rotation = Quaternion.identity;
            }
            else if (transform == fp.fpCameraParent)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
            }
            return NeutralMatrix(transform.parent, fp) * Matrix4x4.TRS(position, rotation, transform.localScale);
        }

        private Camera MakeCamera(string name, Camera source)
        {
            var go = new GameObject(name);
            go.transform.SetParent(rig.transform, false);
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(source);
            camera.enabled = false;
            camera.targetTexture = null;
            camera.ResetAspect();
            camera.ResetProjectionMatrix();
            camera.ResetWorldToCameraMatrix();
            camera.ResetCullingMatrix();
            return camera;
        }

        internal void UpdateDeployment()
        {
            pad.Poll();
            bool canDeploy = plugin.CombatInputAllowed;
            UpdateMenus(canDeploy);
            if (!Actor.dead || Actor.isDeactivated || !canDeploy || Loadout.BlocksInput ||
                !pad.Connected || RespawnRemaining > 0f || (Deployment.Active && !Deployment.Confirmed)) return;
            int cap = GameModeBase.activeGameMode.maxActiveSoldiersAllowed[Actor.team];
            if (cap <= 0 || (hasDeployed && ActorManager.AliveActorsOnTeam(Actor.team).Count >= cap)) return;
            Vector3 position;
            if (!Deployment.TryTeammatePosition(out position))
            {
                SpawnPoint point = Deployment.Active ? Deployment.Point :
                    ActorManager.ClosestSpawnPointOwnedBy(LocalPlayer.actor.Position(), Actor.team);
                if (point == null) return;
                position = point.GetSpawnPosition();
            }
            Actor.SpawnAt(position, Quaternion.Euler(0f, LocalPlayer.controller.transform.eulerAngles.y, 0f), Loadout.Current);
            hasDeployed = true;
            Patches.ForceAnimator(Actor) = true;
        }

        private void UpdateMenus(bool combat)
        {
            Loadout.Update(pad, combat && !Orders.BlocksInput);
            Orders.Update(pad, combat && !Loadout.BlocksInput, pad.Down(Buttons.Right), World);
            Deployment.Update(pad, combat && !Loadout.BlocksInput);
        }

        private WeaponManager.LoadoutSet SelectLoadout()
        {
            Settings settings = plugin.Settings;
            var initial = LoadoutUi.instance != null ? LoadoutUi.instance.loadout : null;
            if (initial == null) initial = Controller.GetLoadout();
            var gear2 = ResolveWeapon(settings.Gear2.Value, WeaponManager.WeaponSlot.LargeGear, initial.gear2);
            return new WeaponManager.LoadoutSet
            {
                primary = ResolveWeapon(settings.Primary.Value, WeaponManager.WeaponSlot.Primary, initial.primary),
                secondary = ResolveWeapon(settings.Secondary.Value, WeaponManager.WeaponSlot.Secondary, initial.secondary),
                gear1 = ResolveWeapon(settings.Gear1.Value, WeaponManager.WeaponSlot.Gear, initial.gear1),
                gear2 = gear2,
                gear3 = gear2?.slot == WeaponManager.WeaponSlot.LargeGear ? null :
                    ResolveWeapon(settings.Gear3.Value, WeaponManager.WeaponSlot.Gear, initial.gear3)
            };
        }

        private WeaponManager.WeaponEntry ResolveWeapon(string name, WeaponManager.WeaponSlot slot,
            WeaponManager.WeaponEntry fallback)
        {
            name = name.Trim();
            if (name.Length == 0) return fallback;
            if (string.Equals(name, "none", StringComparison.OrdinalIgnoreCase)) return null;
            WeaponManager.WeaponEntry entry = WeaponManager.GetWeaponEntryByName(name);
            if (LoadoutPicker.IsAllowed(entry, slot)) return entry;
            plugin.Warn($"Player 2 weapon '{name}' is unavailable for the team or slot. Using the initial selection.");
            return fallback;
        }

        internal void OnSpawn(Vector3 position, Quaternion rotation)
        {
            yaw = rotation.eulerAngles.y;
            pitch = recoilYaw = recoilPitch = 0f;
            aimBlend = weaponKick = weaponKickPitch = 0f;
            lastDamageTime = -1f;
            previousSeat = null;
            chaseCamera = false;
            crouch = false;
            grounded = false;
            Resize(StandingHeight);
            Rebase(position);
            UpdatePose();
        }

        internal void OnDeath()
        {
            readyAt = Time.time + plugin.Settings.RespawnDelay.Value;
            Actor.isScheduledToSpawn = true;
            enabledThisFrame = false;
            wantsMove = false;
            capsule.enabled = false;
        }

        internal void OnSeatEntered(Seat seat)
        {
            previousSeat = seat;
            previousSeatHeading = seat.vehicle.transform.eulerAngles.y;
            yaw = seat.transform.eulerAngles.y;
            pitch = 0f;
            chaseCamera = false;
            vehicleYaw = yaw;
            vehicleCameraFrame = -1;
            DriveInput = Vector2.zero;
            capsule.enabled = false;
        }

        internal void Tick()
        {
            pad.Poll();
            bool combat = plugin.CombatInputAllowed;
            UpdateMenus(combat);
            enabledThisFrame = InputEnabled && pad.Connected && combat && !Loadout.BlocksInput && !Orders.BlocksInput &&
                !Actor.isFrozenGlobal;
            MoveInput = enabledThisFrame ? pad.Move(plugin.Settings.Deadzone) : Vector2.zero;
            LookInput = enabledThisFrame ? pad.Look(plugin.Settings.Deadzone) : Vector2.zero;
            Seat seat = Actor.seat;
            bool driving = seat != null && seat.IsDriverSeat() && !seat.vehicle.isTurret;
            bool flying = driving && seat.vehicle.IsAircraft();
            if (seat != null)
            {
                float heading = seat.vehicle.transform.eulerAngles.y;
                if (seat == previousSeat) yaw += Mathf.DeltaAngle(previousSeatHeading, heading);
                previousSeatHeading = heading;
            }
            previousSeat = seat;
            float dt = Time.deltaTime;
            Vector2 driveAxes = enabledThisFrame && driving ? pad.Drive(plugin.Settings.Deadzone) : Vector2.zero;
            DriveInput = enabledThisFrame && driving && !flying ? Pad.SmoothDrive(DriveInput, driveAxes, dt) : Vector2.zero;
            Vector2 flightAxes = enabledThisFrame && flying ? pad.FlightLook(plugin.Settings.Deadzone) : Vector2.zero;
            FlightInput = new Vector4(driveAxes.x, driveAxes.y, flightAxes.x,
                plugin.Settings.InvertSecond.Value ? -flightAxes.y : flightAxes.y);
            bool aiming = enabledThisFrame && pad.Aim;
            float lookSpeed = driving && !(Actor.activeWeapon is MountedWeapon)
                ? Mathf.Min(105f, plugin.Settings.SecondLook.Value) : plugin.Settings.SecondLook.Value;
            float lookScale = lookSpeed * (aiming ? 0.50f : 1f) * dt;
            if (!flying)
            {
                yaw = Mathf.Repeat(yaw + LookInput.x * lookScale, 360f);
                pitch = Mathf.Clamp(pitch + (plugin.Settings.InvertSecond.Value ? LookInput.y : -LookInput.y) *
                    lookScale, -85f, 85f);
            }
            if (driving && !flying && !(Actor.activeWeapon is MountedWeapon))
            {
                float offset = Mathf.Clamp(Mathf.DeltaAngle(previousSeatHeading, yaw), -85f, 85f);
                if (LookInput == Vector2.zero && !aiming) offset = Mathf.MoveTowards(offset, 0f, dt * 45f);
                yaw = previousSeatHeading + offset;
                pitch = Mathf.Clamp(pitch, -55f, 55f);
                if (LookInput == Vector2.zero && !aiming) pitch = Mathf.MoveTowards(pitch, 0f, dt * 40f);
            }
            recoilPitch = Mathf.MoveTowards(recoilPitch, 0f, dt * 12f);
            recoilYaw = Mathf.MoveTowards(recoilYaw, 0f, dt * 10f);
            weaponKick *= Mathf.Exp(-dt * 17f);
            weaponKickPitch *= Mathf.Exp(-dt * 19f);
            aimBlend = Mathf.MoveTowards(aimBlend, Actor.IsAiming() ? 1f : 0f, dt * 5f);
            Facing = Rotation() * Vector3.forward;

            if (enabledThisFrame && !Actor.fallenOver)
            {
                if (pad.Down(Buttons.Y)) Interact();
                if (pad.Down(Buttons.RB)) Actor.NextWeapon();
                if (pad.Down(Buttons.LB)) Actor.PreviousWeapon();
                if (Actor.IsSeated() && pad.Down(Buttons.Right)) NextSeat();
                if (driving && !flying && pad.Down(Buttons.Down))
                {
                    chaseCamera = !chaseCamera;
                    vehicleYaw = yaw;
                }
            }
            if (Actor.IsOnLadder() && enabledThisFrame &&
                (pad.Down(Buttons.A) || (MoveInput.y > 0.4f && Actor.CanExitLadderAtTop()) ||
                 (MoveInput.y < -0.4f && Actor.CanExitLadderAtBottom())))
                Actor.ExitLadder(false);

            Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
            SwimDirection = flat * new Vector3(MoveInput.x, 0f, MoveInput.y);
            wantsMove = !Actor.IsSeated() && !Actor.fallenOver && !Actor.IsOnLadder() &&
                !Actor.parachuteDeployed && !Actor.isFrozenGlobal && !GameManager.IsPaused();
            if (wantsMove) MoveCapsule(dt, flat);
            else
            {
                if (capsule.enabled) capsule.enabled = false;
                sprint = false;
                Velocity = Vector3.zero;
            }
            UpdatePose();
        }

        private void MoveCapsule(float dt, Quaternion flat)
        {
            Vector3 current = Patches.MovementPosition(Actor);
            if (!capsule.enabled || (current - committedPosition).sqrMagnitude > 0.36f)
                Rebase(current);

            bool holdingCrouch = enabledThisFrame && pad.Held(Buttons.B) && !Actor.IsSwimming();
            if (holdingCrouch && !crouch) { crouch = true; Resize(CrouchingHeight); }
            else if (!holdingCrouch && crouch && CanStand())
            {
                crouch = false;
                Resize(StandingHeight);
            }

            bool swimming = Actor.IsSwimming();
            sprint = enabledThisFrame && pad.Held(Buttons.L3) && MoveInput.y > 0.25f &&
                !crouch && !pad.Aim && !pad.Fire && !swimming;
            float speed = swimming ? 2.1f : crouch ? 2.6f : sprint ? 6.1f : 4.2f;
            Vector3 horizontal = MovementEnabled ? flat * new Vector3(MoveInput.x, 0f, MoveInput.y) *
                (speed * Actor.speedMultiplier) : Vector3.zero;
            grounded = capsule.isGrounded;
            bool wasGrounded = grounded;
            if (grounded || swimming) fallPeak = current.y;
            else fallPeak = Mathf.Max(fallPeak, current.y);
            if (swimming)
                verticalVelocity = Mathf.Clamp((Actor.waterHeight - 1.05f - current.y) * 4f, -3f, 3f);
            else
            {
                if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;
                if (grounded && enabledThisFrame && MovementEnabled && !crouch && pad.Down(Buttons.A))
                    verticalVelocity = 4.6f;
                verticalVelocity = Mathf.Max(-30f, verticalVelocity - 16f * dt);
            }
            Vector3 start = capsule.transform.position;
            CollisionFlags hit = capsule.Move((horizontal + Vector3.up * verticalVelocity) * dt);
            if ((hit & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            grounded = capsule.isGrounded;
            committedPosition = capsule.transform.position;
            Velocity = dt > 0f ? (committedPosition - start) / dt : Vector3.zero;
            walkPhase += horizontal.magnitude * dt * 2.5f;
            CommitMovement();
            if (committedPosition.y < -10000f) Actor.Kill(DamageInfo.Default);
            else if (grounded && !wasGrounded && !swimming) ApplyFallDamage();
        }

        private bool CanStand()
        {
            Vector3 start = capsule.transform.position + Vector3.up * (CrouchingHeight - Radius);
            return !Physics.SphereCast(start, Radius - 0.025f, Vector3.up, out _,
                StandingHeight - CrouchingHeight, TerrainAndVehicleMask, QueryTriggerInteraction.Ignore);
        }

        private void Resize(float height)
        {
            capsule.height = height;
            capsule.center = Vector3.up * (height * 0.5f);
        }

        private void Rebase(Vector3 position)
        {
            capsule.enabled = false;
            capsule.transform.position = position;
            capsule.enabled = !Actor.dead && !Actor.IsSeated() && !Actor.fallenOver;
            if (capsule.enabled)
                foreach (Collider ownCollider in ownColliders)
                    if (ownCollider != null && ownCollider.enabled && ownCollider.gameObject.activeInHierarchy)
                        Physics.IgnoreCollision(capsule, ownCollider);
            committedPosition = position;
            fallPeak = position.y;
            verticalVelocity = -1f;
            Velocity = Vector3.zero;
        }

        private void ApplyFallDamage()
        {
            float drop = fallPeak - committedPosition.y;
            if (drop <= 7f) return;
            float amount = (drop - 7f) / 23f * 100f;
            Actor.Damage(new DamageInfo(DamageInfo.DamageSourceType.FallDamage, null, null)
            {
                healthDamage = amount,
                balanceDamage = amount,
                isPiercing = true,
                point = committedPosition,
                direction = Vector3.down
            });
        }

        internal void CommitMovement()
        {
            if (wantsMove && capsule.enabled && !Actor.dead)
                Patches.MovementPosition(Actor) = committedPosition;
        }

        private void Interact()
        {
            if (Actor.IsSeated()) { Actor.LeaveSeat(false); return; }
            if (Actor.IsOnLadder()) { Actor.ExitLadder(false); return; }
            if (!Actor.CanEnterSeat()) return;

            Vector3 origin = World.transform.position;
            Vehicle vehicle = null;
            float best = 4.5f;
            foreach (Vehicle candidate in ActorManager.VehiclesInRange(Actor.Position(), 8f))
            {
                if (candidate == null || candidate.dead || candidate.isLocked ||
                    (candidate.ownerTeam >= 0 && candidate.ownerTeam != Actor.team) ||
                    candidate.GetEmptySeat(true) == null) continue;
                foreach (Seat seat in candidate.seats)
                {
                    Vector3 offset = seat.transform.position - origin;
                    float distance = offset.magnitude;
                    if (distance >= best || Vector3.Dot(offset.normalized, Facing) < 0.28f) continue;
                    if (Physics.Linecast(origin, seat.transform.position, out var hit, TerrainAndVehicleMask,
                        QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Vehicle>() != candidate)
                        continue;
                    best = distance;
                    vehicle = candidate;
                }
            }
            if (vehicle != null && Actor.EnterVehicle(vehicle)) return;
            foreach (Ladder ladder in ActorManager.instance.ladders)
            {
                Vector3 point = ladder.GetClosestPositionTo(Actor.Position());
                Vector3 offset = point - Actor.Position();
                if (offset.sqrMagnitude > 1.7f || Vector3.Dot(offset.normalized, Facing) < 0.2f ||
                    Physics.Linecast(Actor.Position() + Vector3.up * 0.35f, point, 1,
                        QueryTriggerInteraction.Ignore)) continue;
                Actor.GetOnLadder(ladder);
                capsule.enabled = false;
                return;
            }
        }

        private void NextSeat()
        {
            var seats = Actor.seat.vehicle.seats;
            int current = seats.IndexOf(Actor.seat);
            for (int i = 1; i < seats.Count; i++)
            {
                int next = (current + i) % seats.Count;
                if (seats[next] != null && !seats[next].IsOccupied() && Actor.SwitchSeat(next, false)) return;
            }
        }

        internal bool ReadAction(string action)
        {
            switch (action)
            {
                case "ProjectToGround": case "UseEyeMuzzle": case "Prone": case "IdlePose": return false;
                case "UseMuzzleDirection": case "IsAlert": return true;
                case "OnGround": return grounded || Actor.IsSwimming();
                case "IsMoving": return MoveInput.sqrMagnitude > 0.01f;
                case "IsSprinting": case "UseSprintingAnimation": case "HoldingSprint":
                    return enabledThisFrame && sprint && !Actor.IsSeated();
                case "Crouch": return crouch;
                case "Aiming": return enabledThisFrame && pad.Aim && !Actor.IsSwimming();
                case "WantsToFire": return enabledThisFrame && pad.Fire && !sprint && !Actor.IsSwimming();
                case "Reload": return enabledThisFrame && pad.Held(Buttons.X);
                case "Jump": return enabledThisFrame && pad.Held(Buttons.A);
                case "SwitchFireMode": return enabledThisFrame && pad.Down(Buttons.Left);
                case "Countermeasures": return enabledThisFrame && pad.Down(Buttons.Up);
                case "ForceStopVehicle": return !enabledThisFrame;
                default: return false;
            }
        }

        internal bool IsMovementCapsule(Collider collider) => collider == capsule;

        internal void OnCarriedWeaponShot(Weapon weapon, AudioClip clip)
        {
            float kick = Mathf.Max(0f, weapon.configuration.kickback);
            recoilPitch = Mathf.Max(-12f, recoilPitch - kick * 0.42f);
            recoilYaw = Mathf.Clamp(recoilYaw + UnityEngine.Random.Range(-1f, 1f) * weapon.configuration.randomKick, -6f, 6f);
            weaponKick = Mathf.Min(0.16f, weaponKick + kick * 0.027f);
            weaponKickPitch = Mathf.Min(14f, weaponKickPitch + kick * 1.3f);
            if (clip != null) shotAudio.PlayOneShot(clip, 0.85f);
        }

        internal void NoteProjectile(Projectile projectile)
        {
            if (shotLogs++ >= 4) return;
            Vector3 localOrigin = World.transform.InverseTransformPoint(projectile.transform.position);
            float angle = Vector3.Angle(World.transform.forward, projectile.velocity);
            plugin.LogCombat($"P2 projectile {projectile.sourceWeapon?.weaponEntry?.name}: camera-space origin {localOrigin:F2}, aim deviation {angle:F1} degrees.");
        }

        internal void NoteImpact(Collider collider)
        {
            if (collider == null || impactLogs++ >= 4) return;
            Hitbox hitbox = collider.GetComponent<Hitbox>();
            string target = hitbox != null && hitbox.parent is Actor actor ? $"actor on team {actor.team}" :
                $"{collider.name} (layer {collider.gameObject.layer})";
            plugin.LogCombat($"P2 projectile impacted {target}.");
        }

        internal void NoteDamage(float damage)
        {
            lastDamageTime = Time.time;
            if (damageLogs++ < 4) plugin.LogCombat($"P2 dealt {damage:F1} health damage to an enemy.");
        }

        private Quaternion Rotation() => Quaternion.Euler(pitch + recoilPitch, yaw + recoilYaw, 0f);

        internal void UpdatePose()
        {
            if (World == null || Actor == null) return;
            Seat seat = Actor.seat;
            Vector3 feet = capsule.enabled ? capsule.transform.position : Actor.Position();
            float eye = crouch ? 0.99f : 1.55f;
            Quaternion rotation = Rotation();
            Vector3 position = feet + Vector3.up * eye;
            float fov = hipFov;
            if (Actor.dead || Actor.fallenOver)
            {
                Vector3 center = Actor.Position() + Vector3.up * 0.45f;
                Vector3 arm = -(rotation * Vector3.forward) * 3f + Vector3.up * 1.2f;
                if (Physics.SphereCast(center, 0.15f, arm.normalized, out var hit, arm.magnitude,
                    TerrainAndVehicleMask, QueryTriggerInteraction.Ignore))
                    arm = arm.normalized * Mathf.Max(0f, hit.distance - 0.12f);
                position = center + arm;
                if (arm.sqrMagnitude > 0.0001f) rotation = Quaternion.LookRotation(-arm);
            }
            else if (seat != null)
            {
                var mounted = Actor.activeWeapon as MountedWeapon;
                Camera sight = mounted != null && mounted.overrideCamera != null ? mounted.overrideCamera : seat.camera;
                position = sight != null ? sight.transform.position : seat.transform.position + seat.transform.up * 1.1f;
                if (sight != null)
                {
                    fov = sight.fieldOfView;
                    if (mounted != null || !seat.CanUsePersonalWeapons()) rotation = sight.transform.rotation;
                }
                if (seat.IsDriverSeat() && !seat.vehicle.IsAircraft() && !seat.vehicle.isTurret &&
                    (mounted == null || InChaseCamera))
                {
                    if (vehicleCameraFrame != Time.frameCount)
                    {
                        vehicleCameraFrame = Time.frameCount;
                        vehicleYaw = Mathf.LerpAngle(vehicleYaw, yaw, 1f - Mathf.Exp(-Time.deltaTime * 14f));
                    }
                    rotation = Quaternion.Euler(pitch, vehicleYaw, 0f);
                    if (InChaseCamera)
                    {
                        Vector3 pivot = position + Vector3.up * 0.8f;
                        Vector3 arm = -(rotation * Vector3.forward) * 5.5f + Vector3.up * 1.25f;
                        const int scenery = TerrainAndVehicleMask & ~(1 << 12);
                        if (Physics.SphereCast(pivot, 0.22f, arm.normalized, out var hit, arm.magnitude,
                            scenery, QueryTriggerInteraction.Ignore))
                            arm = arm.normalized * Mathf.Max(0.15f, hit.distance - 0.12f);
                        position = pivot + arm;
                        rotation = Quaternion.LookRotation(pivot + rotation * Vector3.forward * 4f - position);
                    }
                }
            }
            if (!Actor.dead && Actor.activeWeapon != null)
                fov = Mathf.Lerp(fov, Mathf.Min(fov * 0.95f,
                    Actor.activeWeapon.GetActiveSubWeapon().GetAimFov()), aimBlend);
            World.fieldOfView = Mathf.Clamp(fov, 5f, 110f);
            ViewModel.fieldOfView = World.fieldOfView;
            World.transform.SetPositionAndRotation(position, rotation);
            WeaponRoot.localPosition = weaponOffset + new Vector3(0f,
                !Actor.IsAiming() && grounded ? Mathf.Sin(walkPhase) * Mathf.Min(0.018f, Velocity.magnitude * 0.003f) : 0f,
                -weaponKick);
            WeaponRoot.localRotation = weaponRestRotation *
                Quaternion.Euler(pitch * (1f - aimBlend) * 0.1f - weaponKickPitch, 0f, 0f);
        }

        internal bool VisibleFromSecondView(Vector3 position)
        {
            if (World == null || !World.enabled) return false;
            Vector3 point = World.WorldToViewportPoint(position);
            return point.z > 0f && point.z < World.farClipPlane && point.x > -0.1f &&
                point.x < 1.1f && point.y > -0.1f && point.y < 1.1f;
        }

        internal void Deactivate()
        {
            if (Actor == null || Actor.isDeactivated) return;
            if (!Actor.dead) Actor.KillSilently();
            Actor.isScheduledToSpawn = true;
            Actor.Deactivate();
        }

        public void Dispose()
        {
            if (rig != null) UnityEngine.Object.Destroy(rig);
        }
    }
}
