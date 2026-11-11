using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RavenfieldSplit
{
    internal sealed class SplitView : IDisposable
    {
        private const int ViewModelLayer = 18;
        private const int SeatHudMask = 1 << 6;
        private static readonly AccessTools.FieldRef<PostProcessingManager, Camera> GlobalEffectsCamera =
            AccessTools.FieldRefAccess<PostProcessingManager, Camera>("postProcessingCamera");

        private readonly Dictionary<Camera, Rect> originalRects = new Dictionary<Camera, Rect>();
        private readonly Dictionary<Renderer, ShadowCastingMode> shadowModes = new Dictionary<Renderer, ShadowCastingMode>();
        private readonly List<Renderer> maskedRenderers = new List<Renderer>();
        private readonly Dictionary<CanvasGroup, float> hudAlphas = new Dictionary<CanvasGroup, float>();
        private readonly Dictionary<CanvasGroup, float> vehicleHudAlphas = new Dictionary<CanvasGroup, float>();
        private readonly List<CanvasGroup> addedHudGroups = new List<CanvasGroup>();
        private readonly SplitHud hud = new SplitHud();
        private Camera rendering, effectsCamera, maskedSeatCamera;
        private bool seatCameraHadHud;
        private SecondPlayer player;
        private Rect firstRect, secondRect;
        private Renderer[] firstViewModels = Array.Empty<Renderer>();
        private Renderer[] secondViewModels = Array.Empty<Renderer>();
        private Renderer[] firstWeaponRenderers = Array.Empty<Renderer>();
        private Renderer[] secondWeaponRenderers = Array.Empty<Renderer>();
        private Weapon firstWeapon, secondWeapon;
        private Seat firstSeat, hiddenSeat;
        private Actor firstActor;
        private bool effectsEnabled;
        internal bool Active { get; private set; }

        internal SplitView()
        {
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
        }

        internal void Update(SecondPlayer second, bool sideBySide)
        {
            FpsActorController local = LocalPlayer.controller;
            Camera current = local.GetActiveCamera();
            if (current == null) { Suspend(); return; }

            firstRect = sideBySide ? new Rect(0f, 0f, 0.5f, 1f) : new Rect(0f, 0.5f, 1f, 0.5f);
            secondRect = sideBySide ? new Rect(0.5f, 0f, 0.5f, 1f) : new Rect(0f, 0f, 1f, 0.5f);
            player = second;
            if (!Active)
            {
                Active = true;
                HideNativeHud();
                RefreshRenderers(local);
            }
            if (firstActor != local.actor || firstWeapon != local.actor.activeWeapon ||
                firstSeat != local.actor.seat || secondWeapon != second.Actor.activeWeapon)
                RefreshRenderers(local);
            HideNativeSeatHud(local.actor.seat);

            SetFirstRect(local.fpCamera);
            SetFirstRect(local.tpCamera);
            SetFirstRect(local.fpViewModelCamera);
            SetFirstRect(current);
            MaskSeatCameraHud(local.actor.IsSeated() ? current : null);
            DisableGlobalEffects();

            second.UpdatePose();
            ApplyRect(second.World, secondRect);
            ApplyRect(second.ViewModel, secondRect);
            second.World.depth = Mathf.Max(current.depth, local.fpViewModelCamera.depth) + 1f;
            second.ViewModel.depth = second.World.depth + 1f;
            second.World.enabled = true;
            second.ViewModel.enabled = !second.Actor.dead && !second.Actor.fallenOver && !second.InChaseCamera &&
                second.Actor.activeWeapon != null && !second.Actor.activeWeapon.IsMountedWeapon();
        }

        private void SetFirstRect(Camera camera)
        {
            if (camera == null || camera.targetTexture != null) return;
            if (!originalRects.ContainsKey(camera)) originalRects.Add(camera, camera.rect);
            ApplyRect(camera, firstRect);
        }

        private static void ApplyRect(Camera camera, Rect rect)
        {
            camera.rect = rect;
            camera.aspect = (Screen.width * rect.width) / Mathf.Max(1f, Screen.height * rect.height);
        }

        private void DisableGlobalEffects()
        {
            PostProcessingManager manager = PostProcessingManager.instance;
            if (manager == null) return;
            Camera camera = GlobalEffectsCamera(manager);
            if (camera == null) return;
            if (camera != effectsCamera || camera.enabled)
            {
                effectsCamera = camera;
                effectsEnabled = camera.enabled;
            }
            camera.enabled = false;
        }

        private void RefreshRenderers(FpsActorController local)
        {
            firstActor = local.actor;
            firstWeapon = firstActor.activeWeapon;
            firstSeat = firstActor.seat;
            secondWeapon = player.Actor.activeWeapon;
            var renderers = new List<Renderer>(local.GetComponentsInChildren<Renderer>(true));
            renderers.AddRange(local.fpParent.fpCameraRoot.GetComponentsInChildren<Renderer>(true));
            firstViewModels = renderers.ToArray();
            secondViewModels = player.WeaponRoot.GetComponentsInChildren<Renderer>(true);
            firstWeaponRenderers = firstWeapon != null ? firstWeapon.GetComponentsInChildren<Renderer>(true) :
                Array.Empty<Renderer>();
            secondWeaponRenderers = secondWeapon != null ? secondWeapon.GetComponentsInChildren<Renderer>(true) :
                Array.Empty<Renderer>();
        }

        private void BeforeCamera(Camera camera)
        {
            if (!Active || player == null || firstActor == null || player.Actor == null) return;
            RestoreRenderers();
            rendering = camera;
            if (camera == player.World)
            {
                if (!player.Actor.dead && !player.Actor.fallenOver)
                    SetActorShadows(player.Actor, !player.InChaseCamera);
                if (!firstActor.dead && !firstActor.fallenOver)
                    SetActorShadows(firstActor, false);
                if (Patches.IsAimingEquipment(player.Actor)) Mask(secondWeaponRenderers, false);
            }
            else if (camera == player.ViewModel)
            {
                Mask(firstViewModels);
                if (Patches.IsAimingEquipment(player.Actor)) Mask(secondWeaponRenderers, false);
            }
            else
            {
                if ((camera.cullingMask & (1 << ViewModelLayer)) != 0) Mask(secondViewModels);
                FpsActorController local = LocalPlayer.controller;
                if (local != null && (camera == local.fpViewModelCamera || camera == local.fpCamera ||
                    camera == local.GetActiveCamera()) && Patches.IsAimingEquipment(firstActor))
                    Mask(firstWeaponRenderers, false);
            }
        }

        private void AfterCamera(Camera camera)
        {
            if (camera == rendering) RestoreRenderers();
        }

        private void SetActorShadows(Actor actor, bool hide)
        {
            foreach (Renderer renderer in actor.ragdoll.AnimatedRenderers()) SetShadow(renderer, hide);
            foreach (Renderer renderer in actor.accessoryRenderer) SetShadow(renderer, hide);
            if (actor.weaponImposterRenderers == null) return;
            foreach (Renderer[] renderers in actor.weaponImposterRenderers.Values)
                foreach (Renderer renderer in renderers) SetShadow(renderer, hide);
        }

        private void SetShadow(Renderer renderer, bool hide)
        {
            if (renderer == null) return;
            ShadowCastingMode previous = renderer.shadowCastingMode;
            if (!hide && previous != ShadowCastingMode.ShadowsOnly) return;
            if (shadowModes.ContainsKey(renderer)) return;
            shadowModes.Add(renderer, previous);
            renderer.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        private void Mask(Renderer[] renderers, bool viewModelLayerOnly = true)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || renderer.forceRenderingOff ||
                    (viewModelLayerOnly && renderer.gameObject.layer != ViewModelLayer)) continue;
                renderer.forceRenderingOff = true;
                maskedRenderers.Add(renderer);
            }
        }

        private void RestoreRenderers()
        {
            foreach (KeyValuePair<Renderer, ShadowCastingMode> entry in shadowModes)
                if (entry.Key != null) entry.Key.shadowCastingMode = entry.Value;
            foreach (Renderer renderer in maskedRenderers)
                if (renderer != null) renderer.forceRenderingOff = false;
            shadowModes.Clear();
            maskedRenderers.Clear();
            rendering = null;
        }

        private void HideNativeHud()
        {
            IngameUI hud = IngameUI.instance;
            if (hud == null) return;
            Hide(hud.playerHealthElement);
            Hide(hud.loadoutElement);
            Hide(hud.vehicleContainer);
            Hide(hud.vehicleRepairElement);
            if (hud.hitmarker != null) Hide(hud.hitmarker.gameObject);
            if (KillCamera.instance != null) Hide(KillCamera.instance.gameObject);
            if (SpawnCountdownUi.instance != null) Hide(SpawnCountdownUi.instance.gameObject);
        }

        private void HideNativeSeatHud(Seat seat)
        {
            if (ReferenceEquals(hiddenSeat, seat)) return;
            RestoreHud(vehicleHudAlphas);
            hiddenSeat = seat;
            if (seat == null || seat.hud == null) return;
            Hide(seat.hud, vehicleHudAlphas);
            foreach (Canvas canvas in seat.hud.GetComponentsInChildren<Canvas>(true))
                Hide(canvas.gameObject, vehicleHudAlphas);
        }

        private void Hide(GameObject target, Dictionary<CanvasGroup, float> alphas = null)
        {
            if (target == null) return;
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.AddComponent<CanvasGroup>();
                addedHudGroups.Add(group);
            }
            if (alphas == null) alphas = hudAlphas;
            if (alphas.ContainsKey(group)) return;
            alphas.Add(group, group.alpha);
            group.alpha = 0f;
        }

        private static void RestoreHud(Dictionary<CanvasGroup, float> alphas)
        {
            foreach (KeyValuePair<CanvasGroup, float> entry in alphas)
                if (entry.Key != null) entry.Key.alpha = entry.Value;
            alphas.Clear();
        }

        private void MaskSeatCameraHud(Camera camera)
        {
            if (!ReferenceEquals(maskedSeatCamera, camera))
            {
                if (maskedSeatCamera != null && seatCameraHadHud) maskedSeatCamera.cullingMask |= SeatHudMask;
                maskedSeatCamera = camera;
                seatCameraHadHud = camera != null && (camera.cullingMask & SeatHudMask) != 0;
            }
            if (camera != null) camera.cullingMask &= ~SeatHudMask;
        }

        internal void Suspend()
        {
            if (!Active) return;
            Active = false;
            RestoreRenderers();
            if (player != null)
            {
                if (player.World != null) player.World.enabled = false;
                if (player.ViewModel != null) player.ViewModel.enabled = false;
            }
            foreach (KeyValuePair<Camera, Rect> entry in originalRects)
            {
                if (entry.Key == null) continue;
                entry.Key.rect = entry.Value;
                entry.Key.ResetAspect();
            }
            originalRects.Clear();
            if (effectsCamera != null) effectsCamera.enabled = effectsEnabled;
            effectsCamera = null;
            RestoreHud(hudAlphas);
            RestoreHud(vehicleHudAlphas);
            hiddenSeat = null;
            MaskSeatCameraHud(null);
            player = null;
        }

        internal void Draw(Actor first, SecondPlayer second, bool firstConnected, LoadoutPicker firstLoadout,
            bool keyboardControls, DeploymentPicker firstDeployment, SquadOrders firstOrders)
        {
            if (Event.current.type != EventType.Repaint) return;
            string firstDeath = LocalPlayer.controller.hasAcceptedLoadoutAfterDeath ?
                "Waiting for spawn wave" : "Press fire to respawn";
            string secondDeath = second.RespawnRemaining > 0f ?
                $"Respawn in {Mathf.CeilToInt(second.RespawnRemaining)} s" : "Waiting to deploy";
            hud.Draw(GuiRect(firstRect), first, "P1", firstConnected, firstDeath, firstLoadout, keyboardControls,
                firstDeployment, firstOrders, LocalPlayer.controller.GetActiveCamera());
            hud.Draw(GuiRect(secondRect), second.Actor, "P2", second.HasController, secondDeath, second.Loadout,
                false, second.Deployment, second.Orders, second.World, second.ShowHitMarker, second.InChaseCamera);
            GUI.color = Color.black;
            Rect secondPixels = GuiRect(secondRect);
            Rect divider = firstRect.width < 1f
                ? new Rect(secondPixels.x - 1f, 0f, 2f, Screen.height)
                : new Rect(0f, secondPixels.y - 1f, Screen.width, 2f);
            GUI.DrawTexture(divider, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static Rect GuiRect(Rect rect) => new Rect(rect.x * Screen.width,
            (1f - rect.yMax) * Screen.height, rect.width * Screen.width, rect.height * Screen.height);

        public void Dispose()
        {
            Suspend();
            Camera.onPreCull -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            foreach (CanvasGroup group in addedHudGroups)
                if (group != null) UnityEngine.Object.Destroy(group);
        }
    }
}
