using UnityEngine;

namespace RavenfieldSplit
{
    internal sealed class SquadOrders
    {
        internal static readonly string[] Labels = { "Move to marked position", "Regroup", "Recruit aimed ally" };
        private readonly Actor actor;
        private Vector3 origin, direction, destination;
        private bool hasDestination;
        private int frame = -1, consumedFrame = -1;
        private float nextOpen, statusUntil;
        private string status;

        internal bool IsOpen { get; private set; }
        internal bool BlocksInput => IsOpen || consumedFrame == Time.frameCount;
        internal int Selection { get; private set; }
        internal string Status => Time.unscaledTime < statusUntil ? status : null;

        internal SquadOrders(Actor actor) { this.actor = actor; }

        internal void Open(Camera camera)
        {
            if (IsOpen || camera == null || actor.dead || Time.unscaledTime < nextOpen) return;
            origin = camera.transform.position;
            direction = camera.transform.forward;
            hasDestination = ReadRange(camera, out var hit);
            destination = hit.point;
            Selection = 0;
            IsOpen = true;
        }

        internal static bool ReadRange(Camera camera, out RaycastHit hit) =>
            Physics.Raycast(camera.transform.position, camera.transform.forward, out hit, 999f, 4097,
                QueryTriggerInteraction.Ignore);

        internal void Update(Pad pad, bool allowInput, bool shortcut, Camera camera)
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            if (actor.dead) { if (IsOpen) Close(); return; }
            if (!allowInput || (pad != null && !pad.Connected)) return;
            if (!IsOpen)
            {
                if (shortcut && !actor.IsSeated()) Open(camera);
                return;
            }
            bool Pressed(Buttons button, KeyCode key) => pad != null ? pad.Down(button) : Input.GetKeyDown(key);
            if (Pressed(Buttons.B, KeyCode.Backspace)) Close();
            else if (Pressed(Buttons.Up, KeyCode.UpArrow)) Selection = (Selection + Labels.Length - 1) % Labels.Length;
            else if (Pressed(Buttons.Down, KeyCode.DownArrow)) Selection = (Selection + 1) % Labels.Length;
            else if (Pressed(Buttons.A, KeyCode.Return))
            {
                status = Execute();
                statusUntil = Time.unscaledTime + 3f;
                Close();
            }
        }

        private string Execute()
        {
            Squad squad = LocalPlayer.controller.playerSquad;
            if (squad == null || squad.members.Count == 0) return "Squad unavailable";
            if (Selection == 2)
            {
                AiActorController ally = FindRecruit(squad);
                if (ally == null) return "No recruitable ally in view";
                ally.ChangeToSquad(squad);
                return "Ally joined the squad";
            }
            if (squad.aiMembers.Count == 0) return "Recruit an ally first";
            if (Selection == 1)
            {
                squad.Regroup();
                return "Squad regrouping";
            }
            if (!hasDestination) return "Aim at the ground before opening orders";
            squad.Ungroup();
            squad.PlayerOrderMoveTo(destination);
            return "Squad moving to marked position";
        }

        private AiActorController FindRecruit(Squad squad)
        {
            AiActorController best = null;
            float distance = 60f;
            foreach (Actor candidate in ActorManager.AliveActorsOnTeam(actor.team))
            {
                if (!(candidate.controller is AiActorController ally) || Plugin.For(candidate) != null ||
                    squad.members.Contains(ally) || !ally.modifier.canJoinPlayerSquad) continue;
                Vector3 center = candidate.CenterPosition();
                float along = Vector3.Dot(center - origin, direction);
                if (along <= 0.5f || along >= distance || (origin + direction * along - center).sqrMagnitude >= 4f ||
                    Physics.Linecast(origin, center, 1, QueryTriggerInteraction.Ignore)) continue;
                best = ally;
                distance = along;
            }
            return best;
        }

        private void Close()
        {
            IsOpen = false;
            consumedFrame = Time.frameCount;
            nextOpen = Time.unscaledTime + 0.3f;
        }
    }
}
