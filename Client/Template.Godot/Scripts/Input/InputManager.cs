using Godot;
using System;
using Template.Godot.Core;
using Template.Godot.Visuals;
using Template.Shared.Components;
using Template.Shared.Features.Movement;
using Template.Shared.Actions;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;

namespace Template.Godot.Input;

public partial class InputManager : Node
{
    private Vector2 _lastDirection = new Vector2(float.MaxValue, float.MaxValue);
    private float _lastSpeed = -1f;

    // Touch joystick state
    private int _touchIndex = -1;
    private Vector2 _touchStart;
    private const float TouchDeadzone = 20f;
    private const float TouchMaxRadius = 100f;

    public override void _Ready()
    {
        // Register WASD actions
        RegisterKeyAction("move_up", Key.W);
        RegisterKeyAction("move_down", Key.S);
        RegisterKeyAction("move_left", Key.A);
        RegisterKeyAction("move_right", Key.D);
        RegisterKeyAction("sprint", Key.Shift);
        RegisterKeyAction("interact", Key.Space);

        // Register gamepad actions
        if (!InputMap.HasAction("gamepad_interact"))
        {
            InputMap.AddAction("gamepad_interact");
            var ev = new InputEventJoypadButton();
            ev.ButtonIndex = JoyButton.A;
            InputMap.ActionAddEvent("gamepad_interact", ev);
        }
    }

    private void RegisterKeyAction(string name, Key key)
    {
        if (!InputMap.HasAction(name))
        {
            InputMap.AddAction(name);
            var ev = new InputEventKey();
            ev.Keycode = key;
            InputMap.ActionAddEvent(name, ev);
        }
    }

    public override void _Input(InputEvent @event)
    {
        // F key toggles the Family Tree overlay (works even during overlays so you can close it)
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F }
            && !BreedResultOverlay.IsActive)
        {
            FamilyTreeOverlay.Toggle(GetTree());
            GetViewport().SetInputAsHandled();
            return;
        }

        if (BreedResultOverlay.IsActive || FamilyTreeOverlay.IsActive || BuildingInfoOverlay.IsActive || LovePopupOverlay.IsActive) return;

        // Touch input for mobile
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed && _touchIndex == -1)
            {
                _touchIndex = touch.Index;
                _touchStart = touch.Position;
            }
            else if (!touch.Pressed && touch.Index == _touchIndex)
            {
                // Release: check if it was a tap (interact) or drag (movement)
                float dragDist = touch.Position.DistanceTo(_touchStart);
                if (dragDist < TouchDeadzone)
                {
                    SendInteract();
                }
                _touchIndex = -1;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;
        if (BreedResultOverlay.IsActive || FamilyTreeOverlay.IsActive || BuildingInfoOverlay.IsActive || LovePopupOverlay.IsActive) return;

        var localPlayerId = GameManager.Instance.LocalPlayerId;
        if (localPlayerId == 0) return;

        // --- Interaction ---
        if (global::Godot.Input.IsActionJustPressed("ui_accept") ||
            global::Godot.Input.IsActionJustPressed("interact") ||
            global::Godot.Input.IsActionJustPressed("gamepad_interact"))
        {
            SendInteract();
        }

        // --- Movement ---
        var direction = Vector2.Zero;

        // Arrow keys
        if (global::Godot.Input.IsActionPressed("ui_up")) direction.Y -= 1;
        if (global::Godot.Input.IsActionPressed("ui_down")) direction.Y += 1;
        if (global::Godot.Input.IsActionPressed("ui_left")) direction.X -= 1;
        if (global::Godot.Input.IsActionPressed("ui_right")) direction.X += 1;

        // WASD
        if (global::Godot.Input.IsActionPressed("move_up")) direction.Y -= 1;
        if (global::Godot.Input.IsActionPressed("move_down")) direction.Y += 1;
        if (global::Godot.Input.IsActionPressed("move_left")) direction.X -= 1;
        if (global::Godot.Input.IsActionPressed("move_right")) direction.X += 1;

        // Gamepad left stick
        var joyX = global::Godot.Input.GetJoyAxis(0, JoyAxis.LeftX);
        var joyY = global::Godot.Input.GetJoyAxis(0, JoyAxis.LeftY);
        if (Math.Abs(joyX) > 0.2f) direction.X += joyX;
        if (Math.Abs(joyY) > 0.2f) direction.Y += joyY;

        // Touch joystick (drag)
        if (_touchIndex >= 0)
        {
            var touchCurrent = GetViewport().GetMousePosition(); // approximation for current touch
            var touchDelta = touchCurrent - _touchStart;
            if (touchDelta.Length() > TouchDeadzone)
            {
                direction += touchDelta.Normalized() * Math.Min(touchDelta.Length() / TouchMaxRadius, 1f);
            }
        }

        // Clamp and normalize
        if (direction.LengthSquared() > 1f)
            direction = direction.Normalized();

        // Sprint (shift = x2 speed)
        bool sprinting = global::Godot.Input.IsActionPressed("sprint");
        float speed = sprinting ? 20f : 15f;

        var fixedDirection = new Deterministic.GameFramework.Types.Vector2((float)direction.X, (float)direction.Y);

        // Send action if direction or speed changed
        bool dirChanged = direction.DistanceSquaredTo(_lastDirection) > 0.001f;
        bool speedChanged = Math.Abs(speed - _lastSpeed) > 0.01f;

        if (dirChanged || speedChanged)
        {
            _lastDirection = direction;
            _lastSpeed = speed;

            var action = new SetMoveDirectionAction
            {
                Direction = fixedDirection,
                Speed = (int)speed
            };

            if (GameManager.Instance.OfflineMode)
            {
                GameManager.Instance.ScheduleOfflineAction(action, localPlayerId);
            }
            else
            {
                GameManager.Instance.GameClient.Execute(action, localPlayerId);
            }
        }
    }

    private void SendInteract()
    {
        var localPlayerId = GameManager.Instance.LocalPlayerId;
        if (localPlayerId == 0) return;

        if (GameManager.Instance.OfflineMode)
        {
            var interactAction = new InteractAction { UserId = GameManager.Instance.OfflineUserId };
            GameManager.Instance.ScheduleOfflineAction(interactAction, localPlayerId);
        }
        else
        {
            var interactAction = new InteractAction { UserId = GameManager.Instance.GameClient.PlayerId };
            GameManager.Instance.GameClient.Execute(interactAction, localPlayerId);
        }
    }
}
