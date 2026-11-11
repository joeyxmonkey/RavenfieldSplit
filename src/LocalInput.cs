using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Bind = SteelInput.KeyBinds;

namespace RavenfieldSplit
{
    [Flags]
    internal enum Buttons : ushort
    {
        Up = 1, Down = 2, Left = 4, Right = 8, Start = 16, Back = 32,
        L3 = 64, R3 = 128, LB = 256, RB = 512, A = 4096, B = 8192, X = 16384, Y = 32768
    }

    internal sealed class Pad
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct State
        {
            public uint Packet;
            public Buttons Buttons;
            public byte LeftTrigger, RightTrigger;
            public short LX, LY, RX, RY;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState(uint index, out State state);

        private State now, previous;
        private readonly SdlControllers sdl;
        private int frame = -1;
        private bool unavailable;
        internal int Slot { get; }
        internal string Label => sdl == null ? $"XInput slot {Slot}" : $"direct gamepad {Slot}";
        internal Pad(int slot, SdlControllers sdl = null) { Slot = slot; this.sdl = sdl; }
        public bool Connected { get; private set; }
        public bool Held(Buttons button) => (now.Buttons & button) != 0;
        public bool Down(Buttons button) => (now.Buttons & button) != 0 && (previous.Buttons & button) == 0;
        public bool Fire => now.RightTrigger > 35;
        public bool Aim => now.LeftTrigger > 35;
        public Vector2 Move(float deadzone) => Stick(now.LX, now.LY, deadzone);
        public Vector2 Look(float deadzone) => Stick(now.RX, now.RY, deadzone);
        public Vector2 Drive(float deadzone) => new Vector2(DriveAxis(now.LX, deadzone), DriveAxis(now.LY, deadzone));
        public Vector2 FlightLook(float deadzone) =>
            new Vector2(DriveAxis(now.RX, deadzone), DriveAxis(now.RY, deadzone));

        internal static Vector2 SmoothDrive(Vector2 previous, Vector2 target, float deltaTime) =>
            new Vector2(Mathf.MoveTowards(previous.x, target.x * (0.4f + 0.6f * Mathf.Abs(target.x)), 7f * deltaTime),
                Mathf.MoveTowards(previous.y, target.y, 10f * deltaTime));

        private static float DriveAxis(short sample, float deadzone)
        {
            float value = sample / 32768f;
            return Mathf.Sign(value) * Mathf.Clamp01((Mathf.Abs(value) - deadzone) / (1f - deadzone));
        }

        public void Poll()
        {
            if (frame == Time.frameCount || unavailable) return;
            frame = Time.frameCount;
            previous = now;
            try
            {
                bool wasConnected = Connected;
                sdl?.Refresh(frame);
                Connected = sdl == null ? GetState((uint)(Slot - 1), out now) == 0 : sdl.Read(Slot, out now);
                if (!Connected) now = default(State);
                else if (!wasConnected) previous = now;
            }
            catch (DllNotFoundException) { unavailable = true; }
            catch (EntryPointNotFoundException) { unavailable = true; }
        }

        private static Vector2 Stick(short x, short y, float deadzone)
        {
            var v = new Vector2(x / 32768f, y / 32768f);
            float magnitude = v.magnitude;
            return magnitude <= deadzone ? Vector2.zero :
                v / magnitude * Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone));
        }
    }

    internal sealed class PlayerOneInput
    {
        private readonly float[] current = new float[Enum.GetValues(typeof(Bind)).Length];
        private readonly float[] previous = new float[Enum.GetValues(typeof(Bind)).Length];
        private int frame = -1;
        private Vector2 drive;
        internal void Sample(Settings settings, Pad pad)
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            Array.Copy(current, previous, current.Length);
            Array.Clear(current, 0, current.Length);
            if (pad == null)
            {
                drive = Vector2.zero;
                for (int i = 0; i < current.Length; ++i)
                    current[i] = KeyboardAxis(SteelInput.GetInput((Bind)i));
                return;
            }

            pad.Poll();
            Vector2 move = pad.Move(settings.Deadzone);
            Vector2 axes = pad.Drive(settings.Deadzone);
            Vector2 flightLook = pad.FlightLook(settings.Deadzone);
            Actor actor = LocalPlayer.actor;
            bool driving = actor != null && actor.IsDriver();
            bool flying = driving && actor.seat.vehicle.IsAircraft();
            drive = driving && !flying ? Pad.SmoothDrive(drive, axes, Time.deltaTime) : Vector2.zero;
            Vector2 aim = flying ? Vector2.zero : pad.Look(settings.Deadzone) * settings.FirstLook.Value *
                Mathf.Min(3f, 60f * Time.unscaledDeltaTime);
            float y = settings.InvertFirst.Value ? -aim.y : aim.y;
            Set(Bind.Horizontal, -move.x); Set(Bind.Vertical, move.y);
            Set(Bind.AimX, -aim.x); Set(Bind.AimY, y);
            Set(Bind.Fire, pad.Fire); Set(Bind.Aim, pad.Aim);
            Set(Bind.Jump, pad.Held(Buttons.A)); Set(Bind.Crouch, pad.Held(Buttons.B));
            Set(Bind.Reload, pad.Held(Buttons.X)); Set(Bind.Use, pad.Held(Buttons.Y));
            Set(Bind.Sprint, pad.Held(Buttons.L3)); Set(Bind.Kick, pad.Held(Buttons.R3));
            Set(Bind.NextWeapon, pad.Held(Buttons.RB)); Set(Bind.PreviousWeapon, pad.Held(Buttons.LB));
            Set(Bind.FireMode, pad.Held(Buttons.Left)); Set(Bind.Countermeasures, pad.Held(Buttons.Up));
            Set(Bind.ThirdPersonToggle, pad.Held(Buttons.Down)); Set(Bind.OpenLoadout, pad.Held(Buttons.Back));
            Set(Bind.TogglePauseMenu, pad.Held(Buttons.Start));
            Set(Bind.CarSteer, -drive.x); Set(Bind.CarThrottle, drive.y);
            float flightPitch = settings.InvertFirst.Value ? -flightLook.y : flightLook.y;
            Set(Bind.HeliYaw, -axes.x); Set(Bind.HeliThrottle, axes.y);
            Set(Bind.HeliRoll, -flightLook.x); Set(Bind.HeliPitch, flightPitch);
            Set(Bind.PlaneYaw, -axes.x); Set(Bind.PlaneThrottle, axes.y);
            Set(Bind.PlaneRoll, -flightLook.x); Set(Bind.PlanePitch, flightPitch);
        }

        internal float Axis(Bind bind) => current[(int)bind];
        internal bool Held(Bind bind) => Pressed(current[(int)bind]);
        internal bool Down(Bind bind) => Held(bind) && !Pressed(previous[(int)bind]);
        internal bool Up(Bind bind) => !Held(bind) && Pressed(previous[(int)bind]);
        private static bool Pressed(float value) => Mathf.Abs(value) > SteelInput.AXIS_BUTTON_DEADZONE;
        private void Set(Bind bind, float value) => current[(int)bind] = value;
        private void Set(Bind bind, bool value) => Set(bind, value ? 1f : 0f);

        private static float KeyboardAxis(SteelInput.Axis axis)
        {
            if (axis.useUnityAxis)
            {
                if (axis.unityAxis != "Mouse X" && axis.unityAxis != "Mouse Y" &&
                    axis.unityAxis != SteelInput.SCROLL_WHEEL_AXIS) return 0f;
                return axis.GetValueUnity();
            }
            if (SteelInput.ignoreNumberRowBinds && axis.IsNumberRow()) return 0f;
            return (KeyboardKey(axis.posKeyCode) ? 1f : 0f) - (KeyboardKey(axis.negKeyCode) ? 1f : 0f);
        }

        private static bool KeyboardKey(KeyCode key) =>
            key != KeyCode.None && (int)key < (int)KeyCode.JoystickButton0 && Input.GetKey(key);
    }
}
