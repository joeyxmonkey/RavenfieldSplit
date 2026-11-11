using UnityEngine;

namespace RavenfieldSplit
{
    internal sealed class SplitHud
    {
        private static readonly Color Panel = new Color(0.035f, 0.058f, 0.085f, 0.90f);
        private static readonly Color Track = new Color(0.19f, 0.24f, 0.29f, 0.88f);
        private static readonly Color Muted = new Color(0.72f, 0.80f, 0.87f);
        private static readonly Color Teal = new Color(0.34f, 0.91f, 0.77f);
        private static readonly Color Amber = new Color(1f, 0.76f, 0.42f);
        private static readonly Color Red = new Color(1f, 0.35f, 0.35f);
        private GUIStyle title, caption, value, centered;
        private float scale;

        internal void Draw(Rect pane, Actor actor, string playerName, bool connected, string deathMessage,
            LoadoutPicker loadout, bool keyboardControls, DeploymentPicker deployment, SquadOrders orders,
            Camera camera, bool showHitMarker = false, bool chaseCamera = false)
        {
            EnsureStyles();
            GUI.BeginGroup(pane);
            Vector2 center = pane.size * 0.5f;
            if (!connected || actor.dead)
            {
                if (connected && deployment?.Active == true && loadout?.IsOpen != true)
                    DrawDeployment(pane, playerName, deployment, deathMessage, keyboardControls);
                else DrawStatus(pane, playerName, connected ? deathMessage : "Controller disconnected");
            }
            else
            {
                Weapon weapon = actor.activeWeapon != null ? actor.activeWeapon.GetActiveSubWeapon() : null;
                DrawStats(pane, actor, weapon, playerName);
                if (actor.IsSeated()) DrawVehicle(pane, actor);
                if (!actor.fallenOver && loadout?.IsOpen != true && orders?.IsOpen != true)
                {
                    bool mounted = actor.IsSeated() && actor.activeWeapon != null && actor.activeWeapon.IsMountedWeapon();
                    if (mounted && !chaseCamera)
                        DrawMountedSight(center, weapon ?? actor.activeWeapon,
                            actor.IsAiming() || actor.seat.vehicle.isTurret);
                    else if (weapon != null && !(weapon is SquadLeaderKit) &&
                        (!actor.IsAiming() || Patches.IsAimingEquipment(actor)))
                        DrawReticle(center, false);
                    if (showHitMarker) DrawReticle(center, true);
                    TargetTracker tracker = weapon != null ? weapon.targetTracker ?? actor.activeWeapon.targetTracker : null;
                    if (tracker != null && camera != null) DrawTracking(pane, actor, tracker, camera);
                    if (weapon is SquadLeaderKit && camera != null)
                    {
                        DrawReticle(center, false);
                        bool found = SquadOrders.ReadRange(camera, out RaycastHit hit);
                        DrawEquipmentStatus(center, found ? $"{Mathf.RoundToInt(hit.distance)} m   ·   Fire for orders" :
                            "Fire for squad orders", Teal, 35f);
                    }
                }
            }
            if (loadout?.IsOpen == true) DrawLoadout(pane, loadout, playerName, keyboardControls);
            else if (orders?.IsOpen == true) DrawOrders(pane, orders, playerName, keyboardControls);
            else if (orders?.Status != null && !actor.dead) DrawEquipmentStatus(center, orders.Status, Teal, 83f);
            GUI.EndGroup();
        }

        private void DrawDeployment(Rect pane, string playerName, DeploymentPicker deployment,
            string deathMessage, bool keyboardControls)
        {
            var panel = CenteredPanel(pane, 365f, 105f);
            Box(panel, Teal);
            Text(Row(panel, 12f, 6f, 23f), $"{playerName} REDEPLOY", title, Muted);
            Text(Row(panel, 12f, 32f, 29f), $"‹  {deployment.Destination}  ›", value, Color.white);
            string control = keyboardControls ? "Left/right choose   ·   Enter deploy" :
                "D-pad left/right choose   ·   A / Cross deploy";
            Text(Row(panel, 12f, 68f, 25f), deployment.Confirmed ? deathMessage : control, caption, Teal);
        }

        private void DrawOrders(Rect pane, SquadOrders orders, string playerName, bool keyboardControls)
        {
            var panel = CenteredPanel(pane, 365f, 151f);
            Box(panel, Teal);
            Text(Row(panel, 12f, 6f, 23f), $"{playerName} SQUAD ORDERS", title, Color.white);
            for (int i = 0; i < SquadOrders.Labels.Length; i++)
            {
                Rect row = Row(panel, 12f, 35f + i * 25f, 23f);
                if (i == orders.Selection) Paint(row, Track);
                Text(row, SquadOrders.Labels[i], title, i == orders.Selection ? Teal : Muted);
            }
            Text(Row(panel, 12f, 119f, 24f), keyboardControls ? "Up/down select   ·   Enter confirm   ·   Backspace close" :
                "D-pad up/down select   ·   Cross confirm   ·   Circle close", caption, Muted);
        }

        private void DrawTracking(Rect pane, Actor actor, TargetTracker tracker, Camera camera)
        {
            Vector2 center = pane.size * 0.5f;
            if (tracker.HasVehicleTarget())
            {
                Vehicle vehicle = tracker.vehicleTarget;
                Vector3 position = vehicle.targetLockPoint != null ? vehicle.targetLockPoint.position : vehicle.transform.position;
                Vector3 viewport = camera.WorldToViewportPoint(position);
                bool locked = tracker.TargetIsLocked();
                Color color = locked ? Teal : Amber;
                if (viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f)
                {
                    Vector2 p = new Vector2(viewport.x * pane.width, (1f - viewport.y) * pane.height);
                    float size = 12f * scale;
                    Paint(new Rect(p.x - size, p.y - size, 2f * size, 2f), color);
                    Paint(new Rect(p.x - size, p.y + size, 2f * size, 2f), color);
                    Paint(new Rect(p.x - size, p.y - size, 2f, 2f * size), color);
                    Paint(new Rect(p.x + size, p.y - size, 2f, 2f * size), color);
                }
                DrawEquipmentStatus(center, locked ? $"LOCKED   ·   {vehicle.name}" :
                    $"ACQUIRING   ·   {vehicle.name}", color, 57f);
                return;
            }
            string message = tracker.requireAim && !actor.IsAiming() ? "HOLD AIM TO TRACK" :
                tracker.HasPointTarget() && tracker.PointTargetIsAvailable() ? "POINT TARGET AVAILABLE" : "SEARCHING FOR TARGET";
            DrawEquipmentStatus(center, message, Muted, 57f);
        }

        private void DrawEquipmentStatus(Vector2 center, string text, Color color, float offset)
        {
            float width = Mathf.Min(360f * scale, Mathf.Max(150f * scale, caption.CalcSize(new GUIContent(text)).x + 16f * scale));
            var plate = new Rect(center.x - width * 0.5f, center.y + offset * scale, width, 23f * scale);
            Paint(plate, Panel);
            Text(plate, text, centered, color);
        }

        private Rect CenteredPanel(Rect pane, float width, float height)
        {
            width = Mathf.Min(width * scale, pane.width - 20f);
            height *= scale;
            return new Rect((pane.width - width) * 0.5f, (pane.height - height) * 0.5f, width, height);
        }

        private void EnsureStyles()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
                caption = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Clip };
                value = new GUIStyle(title);
                centered = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter };
            }
            scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.6f);
            title.fontSize = Mathf.RoundToInt(15f * scale);
            caption.fontSize = Mathf.RoundToInt(12f * scale);
            value.fontSize = Mathf.RoundToInt(21f * scale);
            centered.fontSize = title.fontSize;
        }

        private void DrawStats(Rect pane, Actor actor, Weapon weapon, string playerName)
        {
            float inset = 14f * scale;
            float width = Mathf.Min(190f * scale, (pane.width - 3f * inset) * 0.5f);
            float height = 67f * scale;
            var health = new Rect(inset, pane.height - height - inset, width, height);
            float ratio = actor.health / Mathf.Max(1f, actor.maxHealth);
            Box(health, ratio > 0.3f ? Teal : Red);
            Text(Row(health, 8f, 5f, 18f), $"{playerName}   HEALTH", caption, Muted);
            Text(Row(health, 8f, 22f, 26f), $"{Mathf.CeilToInt(Mathf.Max(0f, actor.health))} HP", value, Color.white);
            Meter(Row(health, 9f, 53f, 4f), ratio, ratio > 0.3f ? Teal : Red);
            if (weapon == null) return;

            var ammo = new Rect(pane.width - width - inset, health.y, width, height);
            string weaponName = weapon.weaponEntry?.name ?? weapon.displayName;
            Box(ammo, weapon.reloading ? Amber : Teal);
            Text(Row(ammo, 8f, 5f, 18f), weaponName, caption, Muted);
            Text(Row(ammo, 8f, 22f, 27f), weapon.reloading ? "RELOADING" :
                $"{Ammo(weapon.ammo)}  /  {Ammo(weapon.spareAmmo)}", value, weapon.reloading ? Amber : Color.white);
            float fill = weapon.ammo < 0 ? 1f : weapon.ammo / Mathf.Max(1f, weapon.configuration.ammo);
            Meter(Row(ammo, 9f, 53f, 4f), fill, weapon.ammo == 0 ? Red : Teal);
        }

        private void DrawVehicle(Rect pane, Actor actor)
        {
            Seat seat = actor.seat;
            Vehicle vehicle = seat.vehicle;
            float width = Mathf.Min(296f * scale, pane.width - 28f * scale);
            float height = 83f * scale;
            float bottom = (pane.width >= 730f * scale ? 14f : 91f) * scale;
            var panel = new Rect((pane.width - width) * 0.5f, pane.height - height - bottom, width, height);
            string role = seat.IsDriverSeat() && !vehicle.isTurret ? "DRIVER" :
                seat.HasAnyMountedWeapons() ? "GUNNER" : "PASSENGER";
            bool locked = vehicle.IsBeingLocked();
            float hull = vehicle.GetHealthRatio();
            Box(panel, locked || hull < 0.3f ? Red : Amber);
            Text(Row(panel, 8f, 4f, 21f), vehicle.name, title, Color.white);
            string speed = vehicle.isTurret ? string.Empty : $"   •   {Mathf.RoundToInt(vehicle.Velocity().magnitude * 3.6f)} km/h";
            Text(Row(panel, 8f, 25f, 17f), role + speed, caption, Muted);
            string hullText = $"HULL  {Mathf.RoundToInt(100f * Mathf.Clamp01(hull))}%";
            if (locked) hullText += "   •   LOCK WARNING";
            else if (vehicle.burning) hullText += "   •   BURNING";
            else if (vehicle.hasCountermeasures && actor.IsDriver())
                hullText += vehicle.CountermeasuresAreReady() ? "   •   FLARES READY" : "   •   FLARES RECHARGING";
            Text(Row(panel, 8f, 43f, 18f), hullText, caption, locked ? Red : Color.white);
            Meter(Row(panel, 9f, 68f, 4f), hull, hull < 0.3f ? Red : Amber);
        }

        private void DrawStatus(Rect pane, string playerName, string message)
        {
            float width = Mathf.Min(295f * scale, pane.width - 20f);
            var box = new Rect((pane.width - width) * 0.5f, (pane.height - 42f * scale) * 0.5f, width, 42f * scale);
            Box(box, Amber);
            Text(box, $"{playerName}   {message}", centered, Color.white);
        }

        private void DrawLoadout(Rect pane, LoadoutPicker loadout, string playerName, bool keyboardControls)
        {
            float unit = Mathf.Min(scale, pane.height / 245f);
            float width = Mathf.Min(450f * unit, pane.width - 20f);
            float height = 225f * unit;
            var panel = new Rect((pane.width - width) * 0.5f, (pane.height - height) * 0.5f, width, height);
            Box(panel, Teal);
            Text(new Rect(panel.x + 13f * unit, panel.y + 7f * unit, width - 26f * unit, 23f * unit),
                $"{playerName} LOADOUT", title, Color.white);
            for (int i = 0; i < LoadoutPicker.Labels.Length; i++)
            {
                var row = new Rect(panel.x + 10f * unit, panel.y + (33f + i * 28f) * unit, width - 20f * unit, 25f * unit);
                bool selected = i == loadout.SelectedSlot;
                if (selected) Paint(row, Track);
                float keyWidth = Mathf.Min(100f * unit, row.width * 0.34f);
                Text(new Rect(row.x + 5f * unit, row.y, keyWidth, row.height),
                    LoadoutPicker.Labels[i], caption, selected ? Teal : Muted);
                Text(new Rect(row.x + keyWidth + 5f * unit, row.y, row.width - keyWidth - 10f * unit, row.height),
                    loadout.DraftName(i), title, Color.white);
            }
            Text(new Rect(panel.x + 13f * unit, panel.y + 180f * unit, width - 26f * unit, 18f * unit),
                keyboardControls ? "Arrow keys select slots and change items" :
                    "D-pad up/down selects a slot   ·   Left/right changes item", caption, Muted);
            Text(new Rect(panel.x + 13f * unit, panel.y + 198f * unit, width - 26f * unit, 18f * unit),
                keyboardControls ? "Enter  Save for next spawn     Backspace  Cancel" :
                    "A / Cross  Save for next spawn     B / Circle  Cancel", caption, Teal);
        }

        private void DrawMountedSight(Vector2 center, Weapon weapon, bool zoomed)
        {
            if (!zoomed)
            {
                DrawReticle(center, false);
                return;
            }
            float gap = 7f * scale;
            float length = 13f * scale;
            float thickness = Mathf.Max(1f, scale);
            SightLine(new Rect(center.x - gap - length, center.y - thickness * 0.5f, length, thickness));
            SightLine(new Rect(center.x + gap, center.y - thickness * 0.5f, length, thickness));
            SightLine(new Rect(center.x - thickness * 0.5f, center.y - gap - length, thickness, length));
            SightLine(new Rect(center.x - thickness * 0.5f, center.y + gap, thickness, length));
            SightLine(new Rect(center.x - thickness, center.y - thickness, thickness * 2f, thickness * 2f));

            string status = weapon.reloading ? "RELOADING" : weapon.HasLoadedAmmo() ? "READY" : "EMPTY";
            var plate = new Rect(center.x - 55f * scale, center.y + 28f * scale, 110f * scale, 22f * scale);
            Paint(plate, Panel);
            Text(plate, status, centered, status == "READY" ? Teal : Amber);
        }

        private static void SightLine(Rect line)
        {
            Paint(new Rect(line.x - 1f, line.y - 1f, line.width + 2f, line.height + 2f), Color.black);
            Paint(line, Teal);
        }

        private void DrawReticle(Vector2 center, bool hit)
        {
            Matrix4x4 matrix = GUI.matrix;
            if (hit) GUIUtility.RotateAroundPivot(45f, center);
            float start = hit ? 6f : 3f;
            float end = hit ? 13f : 7f;
            Color color = hit ? Amber : Color.white;
            Paint(new Rect(center.x - end, center.y - 1f, end - start, 2f), color);
            Paint(new Rect(center.x + start, center.y - 1f, end - start, 2f), color);
            Paint(new Rect(center.x - 1f, center.y - end, 2f, end - start), color);
            Paint(new Rect(center.x - 1f, center.y + start, 2f, end - start), color);
            GUI.matrix = matrix;
        }

        private Rect Row(Rect box, float inset, float top, float height) =>
            new Rect(box.x + inset * scale, box.y + top * scale, box.width - inset * 2f * scale, height * scale);

        private void Box(Rect box, Color accent)
        {
            Paint(box, Panel);
            Paint(new Rect(box.x, box.y, 2f * scale, box.height), accent);
        }

        private static void Meter(Rect rect, float ratio, Color color)
        {
            Paint(rect, Track);
            rect.width *= Mathf.Clamp01(ratio);
            Paint(rect, color);
        }

        private static void Text(Rect rect, string text, GUIStyle style, Color color)
        {
            GUI.color = Color.white;
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }

        private static void Paint(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static string Ammo(int count) => count < 0 ? "∞" : count.ToString();
    }
}
