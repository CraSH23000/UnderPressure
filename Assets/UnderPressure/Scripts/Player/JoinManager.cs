using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnderPressure
{
    /// <summary>
    /// Drop-in co-op: Space (WASD side), Enter (arrow side), (A), (X) or Start on any gamepad, or a mouse click (the
    /// ghost) joins a new player, up to 4. Players can leave from the pause menu. Lives on the bootstrap object so joined players survive a
    /// level restart.
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
            // a click joins the ghost (one mouse, one ghost); not the click that just closed the pause menu
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Game.Config != null && Game.Config.enableGhost
                && !HasScheme(ControlScheme.Mouse) && Time.frameCount - PauseMenu.ClosedFrame > 1 && Slots.Count < MaxPlayers)
                Join(ControlScheme.Mouse, mouse);
            foreach (var gp in Gamepad.all)
            {
                if (Slots.Count >= MaxPlayers) break;
                if ((gp.buttonSouth.wasPressedThisFrame || gp.buttonWest.wasPressedThisFrame || gp.startButton.wasPressedThisFrame) && !HasDevice(gp))
                    Join(ControlScheme.Gamepad, gp);
            }
        }

        int joinFrame = -1;
        InputDevice joinDevice;

        /// <summary>
        /// Start on a controller with nobody playing on it joins them (it doesn't open the menu). True for that
        /// press: a free controller while there's room, or one that joined on this very frame.
        /// </summary>
        public bool StartJoins(InputDevice d) =>
            d is Gamepad && ((!HasDevice(d) && Slots.Count < MaxPlayers) || (d == joinDevice && joinFrame == Time.frameCount));

        /// <summary>A player leaves: they poof away and their slot (and controls) are free for someone else.</summary>
        public void Leave(int slotIndex)
        {
            var slot = SlotOf(slotIndex);
            if (slot == null) return;
            Slots.Remove(slot);
            var p = Game.Players.Find(pl => pl != null && pl.Index == slotIndex);
            if (p != null)
            {
                Game.Players.Remove(p);
                p.Leave();
            }
            Game.Hud?.SetRoster(Game.Players);
            Banner.Show($"P{slotIndex + 1} LEFT", slot.Color, 80, 0.9f, -40f);
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
            joinFrame = Time.frameCount;
            joinDevice = device;
            Game.Bootstrap.SpawnPlayer(slot, true);
        }

        public static string SchemeLabel(ControlScheme s)
        {
            switch (s)
            {
                case ControlScheme.KeyboardWASD: return "WASD";
                case ControlScheme.KeyboardArrows: return "ARROWS";
                case ControlScheme.Mouse: return "GHOST";
                default: return "PAD";
            }
        }
    }
}
