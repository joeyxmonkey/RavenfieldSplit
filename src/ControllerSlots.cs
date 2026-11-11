namespace RavenfieldSplit
{
    internal static class ControllerSlots
    {
        // Slot numbers are one-based. Slot zero represents keyboard/mouse or no second pad.
        internal static void Assign(int connected, int preferredFirst, int preferredSecond,
            bool keyboard, bool allowKeyboardFallback, out int first, out int second)
        {
            first = 0;
            if (keyboard)
            {
                second = Pick(connected, preferredSecond);
                return;
            }

            if ((connected & Bit(preferredFirst)) != 0) first = preferredFirst;
            else first = Pick(connected & ~Bit(preferredSecond), preferredFirst);
            second = first != 0 ? Pick(connected & ~Bit(first), preferredSecond) : 0;
            if (second != 0 || !allowKeyboardFallback) return;

            first = 0;
            second = Pick(connected, preferredSecond);
        }

        private static int Pick(int connected, int preferred)
        {
            if ((connected & Bit(preferred)) != 0) return preferred;
            for (int slot = 1; slot <= 4; slot++)
                if ((connected & Bit(slot)) != 0) return slot;
            return 0;
        }

        internal static int Bit(int slot) => 1 << (slot - 1);
    }
}
