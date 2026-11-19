using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace UnderPressure
{
    public enum ControlScheme { KeyboardWASD, KeyboardArrows, Gamepad }

    /// <summary>
    /// One player's actions, built in code and scoped to one device (or one half of the keyboard),
    /// so up to four players never read each other's buttons.
    /// </summary>
    public class PlayerInputs : IDisposable
    {
        public readonly InputActionMap Map;
        public readonly InputAction Move, Use, RotateCW, RotateCCW, Jump, Sprint;
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
                    break;

                default:
                    Move.AddBinding("<Gamepad>/leftStick");
                    Move.AddBinding("<Gamepad>/dpad");
                    Jump.AddBinding("<Gamepad>/buttonSouth");
                    Use.AddBinding("<Gamepad>/buttonWest");
                    RotateCCW.AddBinding("<Gamepad>/buttonNorth");
                    RotateCW.AddBinding("<Gamepad>/buttonEast");
                    Sprint.AddBinding("<Gamepad>/leftTrigger");
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
