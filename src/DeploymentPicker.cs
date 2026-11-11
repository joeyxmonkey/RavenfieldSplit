using System.Collections.Generic;
using UnityEngine;

namespace RavenfieldSplit
{
    internal sealed class DeploymentPicker
    {
        private readonly Actor actor, teammate;
        private readonly List<SpawnPoint> points = new List<SpawnPoint>();
        private bool hasLived, wasDead;
        private int selection, frame = -1;
        private SpawnPoint chosenPoint;
        private bool choseTeammate;

        internal DeploymentPicker(Actor actor, Actor teammate)
        {
            this.actor = actor;
            this.teammate = teammate;
            hasLived = !actor.dead;
        }

        internal bool Active => actor != null && actor.dead && hasLived;
        internal bool Confirmed { get; private set; }
        internal string Destination => selection == 0 ? "Other player" :
            selection <= points.Count ? points[selection - 1].shortName : "Nearest friendly point";
        internal SpawnPoint Point => IsFriendly(chosenPoint) ? chosenPoint : NearestFriendly();

        internal void Update(Pad pad, bool allowInput)
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            if (!actor.dead)
            {
                hasLived = true;
                wasDead = false;
                Confirmed = false;
                return;
            }
            if (!hasLived) return;
            if (!wasDead)
            {
                wasDead = true;
                Confirmed = false;
                selection = 1;
                RefreshPoints();
            }
            if (!allowInput || (pad != null && !pad.Connected)) return;
            bool Pressed(Buttons button, KeyCode key) => pad != null ? pad.Down(button) : Input.GetKeyDown(key);
            if (Pressed(Buttons.Left, KeyCode.LeftArrow) || Pressed(Buttons.Right, KeyCode.RightArrow))
            {
                RefreshPoints();
                int count = points.Count + 1;
                selection = (selection + (Pressed(Buttons.Left, KeyCode.LeftArrow) ? count - 1 : 1)) % count;
                Confirmed = false;
            }
            else if (Pressed(Buttons.A, KeyCode.Return))
            {
                RefreshPoints();
                choseTeammate = selection == 0;
                chosenPoint = choseTeammate ? null : points[selection - 1];
                Confirmed = true;
            }
        }

        private void RefreshPoints()
        {
            SpawnPoint prior = selection > 0 && selection <= points.Count ? points[selection - 1] : null;
            points.Clear();
            foreach (SpawnPoint point in ActorManager.instance.spawnPoints)
                if (IsFriendly(point)) points.Add(point);
            if (prior != null)
            {
                int index = points.IndexOf(prior);
                if (index >= 0) selection = index + 1;
            }
            selection = Mathf.Clamp(selection, 0, points.Count);
        }

        private bool IsFriendly(SpawnPoint point) =>
            point != null && point.isActiveAndEnabled && !point.isGhostSpawn && point.owner == actor.team;

        private SpawnPoint NearestFriendly()
        {
            SpawnPoint nearest = null;
            float best = float.PositiveInfinity;
            foreach (SpawnPoint point in ActorManager.instance.spawnPoints)
            {
                if (!IsFriendly(point)) continue;
                float distance = (point.transform.position - actor.Position()).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = point;
            }
            return nearest;
        }

        internal bool TryTeammatePosition(out Vector3 position)
        {
            position = default(Vector3);
            if (!Active || !Confirmed || !choseTeammate || teammate == null || teammate.dead ||
                teammate.team != actor.team || teammate.fallenOver || teammate.IsSeated() ||
                teammate.IsOnLadder() || teammate.IsSwimming() || teammate.parachuteDeployed ||
                !teammate.controller.OnGround()) return false;

            Vector3 anchor = teammate.Position();
            Vector3 direction = teammate.controller.FacingDirection();
            direction.y = 0f;
            direction = direction.sqrMagnitude < 0.01f ? Vector3.forward : direction.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            return TryGround(anchor, -direction * 2.2f, out position) ||
                TryGround(anchor, side * 2.2f, out position) || TryGround(anchor, -side * 2.2f, out position);
        }

        private static bool TryGround(Vector3 anchor, Vector3 offset, out Vector3 position)
        {
            const int groundMask = 2232321;
            position = default(Vector3);
            Vector3 probe = anchor + offset + Vector3.up * 1.4f;
            if (!Physics.Raycast(probe, Vector3.down, out var hit, 3.1f, groundMask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < 0.75f || WaterLevel.IsInWater(hit.point + Vector3.up * 0.35f) ||
                Physics.Linecast(anchor + Vector3.up, hit.point + Vector3.up, groundMask, QueryTriggerInteraction.Ignore) ||
                Physics.CheckCapsule(hit.point + Vector3.up * 0.4f, hit.point + Vector3.up * 1.5f,
                    0.32f, groundMask | Hitbox.ALL_HITBOX_LAYER_MASK, QueryTriggerInteraction.Ignore)) return false;
            position = hit.point + Vector3.up * 0.06f;
            return true;
        }
    }
}
