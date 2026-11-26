using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace UnderPressure
{
    public enum ControlScheme { KeyboardWASD, KeyboardArrows, Gamepad, Mouse }

    /// <summary>
    /// One player's actions, built in code and scoped to one device (or one half of the keyboard),
    /// so up to four players never read each other's buttons.
    /// </summary>
    public class PlayerInputs : IDisposable
    {
        public readonly InputActionMap Map;
        public readonly InputAction Move, Use, RotateCW, RotateCCW, Jump, Sprint, Inventory;
        public readonly InputAction[] ItemButtons = new InputAction[3];

        /// <summary>What the item buttons and the inventory button are called for this player's controls.</summary>
        public string ItemLabel(int i) => Scheme == ControlScheme.Mouse ? "" : Scheme == ControlScheme.Gamepad ? new[] { "RT", "LB", "RB" }[i]
            : Scheme == ControlScheme.KeyboardWASD ? new[] { "1", "2", "3" }[i] : new[] { "End", "PgDn", "PgUp" }[i];
        public string InventoryLabel => Scheme == ControlScheme.Mouse ? "" : Scheme == ControlScheme.Gamepad ? "Select" : Scheme == ControlScheme.KeyboardWASD ? "Tab" : "Backspace";
        public readonly ControlScheme Scheme;

        public PlayerInputs(ControlScheme scheme, InputDevice device)
        {
            Scheme = scheme;
            Map = new InputActionMap("Player_" + scheme);
            Move = Map.AddAction("Move", InputActionType.Value);
            Use = Map.AddAction("Use", InputActionType.Button);
            RotateCW = Map.AddAction("RotateCW", InputActionType.Button);
            RotateCCW = Map.AddAction("RotateCCW", InputActionType.Button);
            Jump = Map.AddAction("Jump", InputActionType.Button);
            Sprint = Map.AddAction("Sprint", InputActionType.Button);
            Inventory = Map.AddAction("Inventory", InputActionType.Button);
            for (int i = 0; i < ItemButtons.Length; i++) ItemButtons[i] = Map.AddAction("Item" + (i + 1), InputActionType.Button);

            switch (scheme)
            {
                case ControlScheme.KeyboardWASD:
                    Move.AddCompositeBinding("2DVector")
                        .With("up", "<Keyboard>/w").With("down", "<Keyboard>/s")
                        .With("left", "<Keyboard>/a").With("right", "<Keyboard>/d");
                    Jump.AddBinding("<Keyboard>/space");
                    Use.AddBinding("<Keyboard>/e");
                    RotateCCW.AddBinding("<Keyboard>/f");
                    RotateCW.AddBinding("<Keyboard>/g");
                    Sprint.AddBinding("<Keyboard>/leftShift");
                    Inventory.AddBinding("<Keyboard>/tab");
                    ItemButtons[0].AddBinding("<Keyboard>/1");
                    ItemButtons[1].AddBinding("<Keyboard>/2");
                    ItemButtons[2].AddBinding("<Keyboard>/3");
                    break;

                case ControlScheme.KeyboardArrows:
                    // right-hand cluster around Enter (numpad alternatives in brackets)
                    Move.AddCompositeBinding("2DVector")
                        .With("up", "<Keyboard>/upArrow").With("down", "<Keyboard>/downArrow")
                        .With("left", "<Keyboard>/leftArrow").With("right", "<Keyboard>/rightArrow");
                    Jump.AddBinding("<Keyboard>/rightCtrl");
                    Jump.AddBinding("<Keyboard>/numpad0");
                    Use.AddBinding("<Keyboard>/enter");
                    Use.AddBinding("<Keyboard>/numpadEnter");
                    RotateCCW.AddBinding("<Keyboard>/semicolon");
                    RotateCCW.AddBinding("<Keyboard>/numpad1");
                    RotateCW.AddBinding("<Keyboard>/quote");
                    RotateCW.AddBinding("<Keyboard>/numpad2");
                    Sprint.AddBinding("<Keyboard>/rightShift");
                    Sprint.AddBinding("<Keyboard>/numpadPeriod");
                    Inventory.AddBinding("<Keyboard>/backspace");
                    ItemButtons[0].AddBinding("<Keyboard>/end");
                    ItemButtons[1].AddBinding("<Keyboard>/pageDown");
                    ItemButtons[2].AddBinding("<Keyboard>/pageUp");
                    break;

                case ControlScheme.Mouse:
                    // the ghost: it floats after the cursor (read directly), so there's no Move; scroll rotates (also read directly)
                    Use.AddBinding("<Mouse>/leftButton");
                    Jump.AddBinding("<Mouse>/rightButton"); // drop / let go
                    break;

                default:
                    Move.AddBinding("<Gamepad>/leftStick");
                    Move.AddBinding("<Gamepad>/dpad");
                    Jump.AddBinding("<Gamepad>/buttonSouth");
                    Use.AddBinding("<Gamepad>/buttonWest");
                    RotateCCW.AddBinding("<Gamepad>/buttonNorth");
                    RotateCW.AddBinding("<Gamepad>/buttonEast");
                    Sprint.AddBinding("<Gamepad>/leftTrigger");
                    Inventory.AddBinding("<Gamepad>/select");
                    ItemButtons[0].AddBinding("<Gamepad>/rightTrigger");
                    ItemButtons[1].AddBinding("<Gamepad>/leftShoulder");
                    ItemButtons[2].AddBinding("<Gamepad>/rightShoulder");
                    break;
            }

            if (device != null) Map.devices = new ReadOnlyArray<InputDevice>(new[] { device });
            Map.Enable();
        }

        public Vector2 MoveValue => Vector2.ClampMagnitude(Move.ReadValue<Vector2>(), 1f);

        public void Dispose()
        {
            Map.Disable();
            Map.Dispose();
        }
    }
}
