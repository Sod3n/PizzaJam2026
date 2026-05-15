using Godot;
using System;
using Template.Godot.Core;
using Template.Godot.Visuals;
using Template.Shared.Components;
using Template.Shared.Features.Movement;
using Template.Shared.Actions;
using Template.Shared.GameData;
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
    private bool _touchHoldFiring;
    private const float TouchDeadzone = 20f;
    private const float TouchMaxRadius = 100f;

    private int _holdRepeatTicks;

    private static bool TryGetLocalPlayerStats(int localPlayerId, out bool isHelperPlayer, out int petCount, out int targetCowPetCount)
    {
        isHelperPlayer = false;
        petCount = 0;
        targetCowPetCount = 0;
        var state = Deterministic.GameFramework.Reactive.ReactiveSystem.Instance?.BoundState;
        if (state == null) return false;
        var ent = new Entity(localPlayerId);
        if (!state.HasComponent<PlayerStateComponent>(ent)) return false;
        var ps = state.GetComponent<PlayerStateComponent>(ent);
        petCount = ps.PetCount;
        isHelperPlayer = state.HasComponent<HelperPlayerComponent>(ent);
        if (ps.InteractionTarget != Entity.Null && state.HasComponent<CowComponent>(ps.InteractionTarget))
            targetCowPetCount = state.GetComponent<CowComponent>(ps.InteractionTarget).PetCount;
        return true;
    }

    private static bool IsLocalPlayerCaught(int localPlayerId)
    {
        var state = Deterministic.GameFramework.Reactive.ReactiveSystem.Instance?.BoundState;
        if (state == null) return false;
        var ent = new Entity(localPlayerId);
        return state.HasComponent<CaughtComponent>(ent);
    }

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
        // ESC key toggles the Settings overlay (works even during other overlays so you can open/close it)
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
            && !BreedResultOverlay.IsActive && !FamilyTreeOverlay.IsActive
            && !LovePopupOverlay.IsActive && !CraftingOverlay.IsActive)
        {
            SettingsOverlay.Toggle(GetTree());
            GetViewport().SetInputAsHandled();
            return;
        }

        // C key toggles the Crafting Recipes overlay
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.C }
            && !BreedResultOverlay.IsActive && !SettingsOverlay.IsActive)
        {
            CraftingOverlay.Toggle(GetTree());
            GetViewport().SetInputAsHandled();
            return;
        }

        if (BreedResultOverlay.IsActive || FamilyTreeOverlay.IsActive || LovePopupOverlay.IsActive || CraftingOverlay.IsActive || SettingsOverlay.IsActive) return;

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
                float dragDist = touch.Position.DistanceTo(_touchStart);
                if (dragDist < TouchDeadzone && !_touchHoldFiring)
                {
                    SendInteract();
                }
                _touchIndex = -1;
                _touchHoldFiring = false;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;
        if (BreedResultOverlay.IsActive || FamilyTreeOverlay.IsActive || LovePopupOverlay.IsActive || CraftingOverlay.IsActive || SettingsOverlay.IsActive) return;

        var localPlayerId = GameManager.Instance.LocalPlayerId;
        if (localPlayerId == 0) return;

        if (IsLocalPlayerCaught(localPlayerId))
        {
            _holdRepeatTicks = 0;
            _touchHoldFiring = false;
            return;
        }

        // --- Interaction ---
        bool interactJustPressed = global::Godot.Input.IsActionJustPressed("ui_accept") ||
                                   global::Godot.Input.IsActionJustPressed("interact") ||
                                   global::Godot.Input.IsActionJustPressed("gamepad_interact");
        bool interactHeld = global::Godot.Input.IsActionPressed("ui_accept") ||
                            global::Godot.Input.IsActionPressed("interact") ||
                            global::Godot.Input.IsActionPressed("gamepad_interact");
        bool touchHeldStationary = _touchIndex >= 0 &&
                                   GetViewport().GetMousePosition().DistanceTo(_touchStart) < TouchDeadzone;

        TryGetLocalPlayerStats(localPlayerId, out bool isHelperPlayer, out int petCount, out int targetCowPetCount);
        int holdThreshold = isHelperPlayer ? Balance.HelperPlayer.HoldRepeatThreshold : Balance.Player.HoldRepeatThreshold;
        // Pets assigned to the cow being milked accelerate the player's auto-fire as if they
        // were the player's own pets — they're "helping" the milker click faster.
        holdThreshold = System.Math.Max(
            Balance.Pets.HoldRepeatFloor,
            holdThreshold - (petCount + targetCowPetCount) * Balance.Pets.HoldRepeatReductionPerPet);

        if (interactJustPressed)
        {
            SendInteract(isHoldRepeat: false);
            _holdRepeatTicks = 0;
        }
        else if (interactHeld || touchHeldStationary)
        {
            _holdRepeatTicks++;
            if (_holdRepeatTicks >= holdThreshold)
            {
                SendInteract(isHoldRepeat: true);
                _holdRepeatTicks = 0;
                if (touchHeldStationary) _touchHoldFiring = true;
            }
        }
        else
        {
            _holdRepeatTicks = 0;
            _touchHoldFiring = false;
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

        bool sprinting = global::Godot.Input.IsActionPressed("sprint");
        float baseWalk = isHelperPlayer ? Balance.HelperPlayer.WalkSpeed : Balance.Player.WalkSpeed;
        float baseSprint = isHelperPlayer ? Balance.HelperPlayer.SprintSpeed : Balance.Player.SprintSpeed;
        float speed = (sprinting ? baseSprint : baseWalk) + Balance.Pets.SpeedPerPet * petCount;

        var fixedDirection = new Deterministic.GameFramework.Types.Vector2((float)direction.X, (float)direction.Y);

        // Send action if direction or speed changed, OR re-send every frame while the player
        // is actively moving — the navmesh slide rewrites body.Velocity, so without a fresh
        // action each tick the player would keep drifting along the wall after the corner ends.
        bool dirChanged = direction.DistanceSquaredTo(_lastDirection) > 0.001f;
        bool speedChanged = Math.Abs(speed - _lastSpeed) > 0.01f;
        bool moving = direction.LengthSquared() > 0.001f;

        if (dirChanged || speedChanged || moving)
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

    private void SendInteract(bool isHoldRepeat = false)
    {
        var localPlayerId = GameManager.Instance.LocalPlayerId;
        if (localPlayerId == 0) return;

        if (GameManager.Instance.OfflineMode)
        {
            var interactAction = new InteractAction { UserId = GameManager.Instance.OfflineUserId, IsHoldRepeat = isHoldRepeat };
            GameManager.Instance.ScheduleOfflineAction(interactAction, localPlayerId);
        }
        else
        {
            var interactAction = new InteractAction { UserId = GameManager.Instance.GameClient.PlayerId, IsHoldRepeat = isHoldRepeat };
            GameManager.Instance.GameClient.Execute(interactAction, localPlayerId);
        }
    }
}
