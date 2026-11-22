using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnderPressure
{
    /// <summary>
    /// Drop-in co-op: Space (WASD side), Enter (arrow side) or (A) on any gamepad joins a new player, up to 4.
    /// Lives on the bootstrap object so joined players survive a level restart.
    /// </summary>
    public class JoinManager : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        /// <summary>Player colors come from the one color list (CritterLook.Colors), limited to the ones marked 'player'.
        /// Slots P1-P4 default to the first four.</summary>
        public static bool IsPlayerColor(int i) => i >= 0 && i < CritterLook.Colors.Length && CritterLook.Colors[i].player;

        /// <summary>Default color per slot (before anyone has picked).</summary>
        public static Color[] PlayerColors => new[] { CritterLook.Colors[0].color, CritterLook.Colors[1].color, CritterLook.Colors[2].color, CritterLook.Colors[3].color };

        public static JoinManager Instance { get; private set; }

        public class Slot
        {
            public int index;
            public ControlScheme scheme;
            public InputDevice device;
            public int colorIndex = -1; // into CritterLook.Colors (a player color)
            public Color Color => CritterLook.Colors[Mathf.Clamp(colorIndex, 0, CritterLook.Colors.Length - 1)].color;
            public string ColorName => CritterLook.Colors[Mathf.Clamp(colorIndex, 0, CritterLook.Colors.Length - 1)].name;
        }

        void OnEnable() => Instance = this;

        public Slot SlotOf(int index) => Slots.Find(s => s.index == index);

        bool Taken(int colorIndex, int exceptSlot) => Slots.Exists(s => s.index != exceptSlot && s.colorIndex == colorIndex);

        /// <summary>Give a slot its color: the one it picked last time if nobody has it, else its default, else the first free one.</summary>
        public void AssignColor(Slot slot)
        {
            if (IsPlayerColor(slot.colorIndex) && !Taken(slot.colorIndex, slot.index)) return;
            int def = slot.index % 4;
            int saved = PlayerPrefs.GetInt("up_pcolor" + slot.index, def);
            foreach (int c in new[] { saved, def })
                if (IsPlayerColor(c) && !Taken(c, slot.index)) { slot.colorIndex = c; return; }
            for (int c = 0; c < CritterLook.Colors.Length; c++) if (IsPlayerColor(c) && !Taken(c, slot.index)) { slot.colorIndex = c; return; }
            slot.colorIndex = def;
        }

        /// <summary>Step a player's color left or right, skipping colors other players have. Returns false if nothing's free.</summary>
        public bool CycleColor(int slotIndex, int dir)
        {
            var slot = SlotOf(slotIndex);
            if (slot == null) return false;
            int n = CritterLook.Colors.Length;
            for (int k = 1; k < n; k++)
            {
                int c = ((slot.colorIndex + dir * k) % n + n) % n;
                if (!IsPlayerColor(c) || Taken(c, slotIndex)) continue;
                slot.colorIndex = c;
                PlayerPrefs.SetInt("up_pcolor" + slotIndex, c);
                PlayerPrefs.Save();
                return true;
            }
            return false;
        }

        public readonly List<Slot> Slots = new List<Slot>();

        void Update()
        {
            if (Game.Manager == null || Game.Manager.Paused || Slots.Count >= MaxPlayers) return; // joining between chambers is fine too

            var kb = Keyboard.current;
            if (kb != null)
            {
                if ((kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) && !HasScheme(ControlScheme.KeyboardWASD))
                    Join(ControlScheme.KeyboardWASD, kb);
                else if ((kb.enterKey.wasPressedThisFrame || kb.rightCtrlKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                         && !HasScheme(ControlScheme.KeyboardArrows))
                    Join(ControlScheme.KeyboardArrows, kb);
            }
            foreach (var gp in Gamepad.all)
            {
                if (Slots.Count >= MaxPlayers) break;
                if ((gp.buttonSouth.wasPressedThisFrame || gp.buttonWest.wasPressedThisFrame) && !HasDevice(gp)) Join(ControlScheme.Gamepad, gp);
            }
        }

        bool HasScheme(ControlScheme s)
        {
            foreach (var slot in Slots) if (slot.scheme == s) return true;
            return false;
        }

        bool HasDevice(InputDevice d)
        {
            foreach (var slot in Slots) if (slot.device == d) return true;
            return false;
        }

        void Join(ControlScheme scheme, InputDevice device)
        {
            int index = 0;
            while (Slots.Exists(s => s.index == index)) index++;
            var slot = new Slot { index = index, scheme = scheme, device = device };
            Slots.Add(slot);
            Game.Bootstrap.SpawnPlayer(slot, true);
        }

        public static string SchemeLabel(ControlScheme s)
        {
            switch (s)
            {
                case ControlScheme.KeyboardWASD: return "WASD";
                case ControlScheme.KeyboardArrows: return "ARROWS";
                default: return "PAD";
            }
        }
    }
}
