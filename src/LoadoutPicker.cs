using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Entry = WeaponManager.WeaponEntry;
using Slot = WeaponManager.WeaponSlot;

namespace RavenfieldSplit
{
    internal sealed class LoadoutPicker
    {
        internal static readonly string[] Labels = { "PRIMARY", "SIDEARM", "GEAR 1", "GEAR 2", "GEAR 3" };
        private static readonly Slot[] Slots = { Slot.Primary, Slot.Secondary, Slot.Gear, Slot.LargeGear, Slot.Gear };
        private static readonly Func<Entry, bool> CanUse =
            AccessTools.MethodDelegate<Func<Entry, bool>>(AccessTools.DeclaredMethod(typeof(LoadoutUi), "CanUseWeaponEntry"));
        private readonly List<Entry>[] choices = new List<Entry>[Slots.Length];
        private readonly Entry[] draft = new Entry[Slots.Length];
        private readonly Action<WeaponManager.LoadoutSet> save;
        private int frame = -1;
        private int consumedFrame = -1;

        internal WeaponManager.LoadoutSet Current { get; }
        internal bool IsOpen { get; private set; }
        internal int SelectedSlot { get; private set; }
        internal bool BlocksInput => IsOpen || consumedFrame == Time.frameCount;
        internal bool UsesLargeGear => draft[3]?.slot == Slot.LargeGear;

        internal LoadoutPicker(WeaponManager.LoadoutSet initial, Action<WeaponManager.LoadoutSet> save)
        {
            Current = initial;
            this.save = save;
        }

        internal static bool IsAllowed(Entry entry, Slot slot) =>
            entry != null && entry.prefab != null && LoadoutUi.ShouldAppearInSlot(entry, slot) && CanUse(entry);

        internal string DraftName(int slot) => slot == 4 && UsesLargeGear ? "Occupied by large gear" :
            draft[slot]?.name ?? "Empty";

        internal void Update(Pad pad, bool allowInput, bool keyboardOpen = false)
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            if (!allowInput || (pad != null && !pad.Connected)) return;
            bool openPressed = pad != null ? pad.Down(Buttons.Back) : keyboardOpen;
            if (!IsOpen)
            {
                if (openPressed) Open();
                return;
            }
            bool Pressed(Buttons button, KeyCode key) => pad != null ? pad.Down(button) : Input.GetKeyDown(key);
            if (openPressed || Pressed(Buttons.B, KeyCode.Backspace)) Close();
            else if (Pressed(Buttons.A, KeyCode.Return))
            {
                Current.primary = draft[0];
                Current.secondary = draft[1];
                Current.gear1 = draft[2];
                Current.gear2 = draft[3];
                Current.gear3 = UsesLargeGear ? null : draft[4];
                save(Current);
                Close();
            }
            else if (Pressed(Buttons.Up, KeyCode.UpArrow) || Pressed(Buttons.Down, KeyCode.DownArrow))
            {
                int count = UsesLargeGear ? 4 : 5;
                SelectedSlot = (SelectedSlot + (Pressed(Buttons.Up, KeyCode.UpArrow) ? count - 1 : 1)) % count;
            }
            else if (Pressed(Buttons.Left, KeyCode.LeftArrow) || Pressed(Buttons.Right, KeyCode.RightArrow))
            {
                List<Entry> entries = choices[SelectedSlot];
                int index = entries.IndexOf(draft[SelectedSlot]);
                int step = Pressed(Buttons.Left, KeyCode.LeftArrow) ? entries.Count - 1 : 1;
                draft[SelectedSlot] = entries[(Mathf.Max(0, index) + step) % entries.Count];
            }
        }

        private void Open()
        {
            draft[0] = Current.primary;
            draft[1] = Current.secondary;
            draft[2] = Current.gear1;
            draft[3] = Current.gear2;
            draft[4] = Current.gear3;
            for (int i = 0; i < choices.Length; i++)
            {
                var entries = new List<Entry> { null };
                foreach (Entry entry in WeaponManager.instance.allWeapons)
                    if (IsAllowed(entry, Slots[i])) entries.Add(entry);
                if (draft[i] != null && !entries.Contains(draft[i])) entries.Add(draft[i]);
                choices[i] = entries;
            }
            SelectedSlot = 0;
            IsOpen = true;
        }

        private void Close()
        {
            IsOpen = false;
            consumedFrame = Time.frameCount;
        }
    }
}
