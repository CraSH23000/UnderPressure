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

        public static readonly Color[] PlayerColors =
        {
            new Color(0.25f, 0.75f, 1.0f),  // P1 sky blue
            new Color(1.0f, 0.42f, 0.7f),   // P2 bubblegum
            new Color(1.0f, 0.62f, 0.18f),  // P3 tangerine
            new Color(0.68f, 0.45f, 1.0f),  // P4 lilac
        };

        public class Slot
        {
            public int index;
            public ControlScheme scheme;
            public InputDevice device;
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
