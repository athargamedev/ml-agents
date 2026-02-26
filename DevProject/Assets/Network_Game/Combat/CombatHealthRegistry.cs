using System;
using System.Collections.Generic;

namespace Network_Game.Combat
{
    /// <summary>
    /// Lightweight runtime registry for active CombatHealth instances.
    /// Keeps the debug UI event-driven and avoids repeated scene scans.
    /// </summary>
    public static class CombatHealthRegistry
    {
        public static event Action<CombatHealth> OnRegistered;
        public static event Action<CombatHealth> OnUnregistered;

        private static readonly List<CombatHealth> s_Items = new List<CombatHealth>(32);

        public static IReadOnlyList<CombatHealth> Items => s_Items;

        public static void Register(CombatHealth health)
        {
            if (health == null)
            {
                return;
            }

            CleanupNulls();
            if (s_Items.Contains(health))
            {
                return;
            }

            s_Items.Add(health);
            OnRegistered?.Invoke(health);
        }

        public static void Unregister(CombatHealth health)
        {
            if (health == null)
            {
                return;
            }

            if (!s_Items.Remove(health))
            {
                return;
            }

            OnUnregistered?.Invoke(health);
        }

        public static void CopyTo(List<CombatHealth> target)
        {
            if (target == null)
            {
                return;
            }

            CleanupNulls();
            target.Clear();
            target.AddRange(s_Items);
        }

        private static void CleanupNulls()
        {
            for (int i = s_Items.Count - 1; i >= 0; i--)
            {
                if (s_Items[i] != null)
                {
                    continue;
                }

                s_Items.RemoveAt(i);
            }
        }
    }
}
