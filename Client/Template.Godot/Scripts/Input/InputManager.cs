using Godot;
using System;
using Template.Godot.Core;
using Template.Shared.Components;
using Template.Shared.Features.Movement;
using Template.Shared.Actions;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;

namespace Template.Godot.Input;

public partial class InputManager : Node
{
	private Vector2 _lastDirection = new Vector2(float.MaxValue, float.MaxValue);

	public override void _Process(double delta)
	{
		// 1. Check prerequisites
		if (GameManager.Instance == null || !GameManager.Instance.IsGameRunning) return;
		
		var localPlayerId = GameManager.Instance.LocalPlayerId;
		if (localPlayerId == 0) return;

		// 2. Poll Input
		
		// Interaction
		if (global::Godot.Input.IsActionJustPressed("ui_accept"))
		{
			var interactAction = new InteractAction 
			{ 
				UserId = GameManager.Instance.GameClient.PlayerId 
			};
			GameManager.Instance.GameClient.Execute(interactAction, localPlayerId);
		}

		// Movement
		var direction = Vector2.Zero;

		if (global::Godot.Input.IsActionPressed("ui_up")) direction.Y -= 1;
		if (global::Godot.Input.IsActionPressed("ui_down")) direction.Y += 1;
		if (global::Godot.Input.IsActionPressed("ui_left")) direction.X -= 1;
		if (global::Godot.Input.IsActionPressed("ui_right")) direction.X += 1;

		if (direction != Vector2.Zero)
		{
			direction = direction.Normalized();
		}
		
		// 3. Send Action if changed
		if (direction.DistanceSquaredTo(_lastDirection) > 0.001f)
		{
			_lastDirection = direction;
			
			// Convert Godot.Vector2 to Deterministic.GameFramework.Types.Vector2
			var fixedDirection = new Deterministic.GameFramework.Types.Vector2((float)direction.X, (float)direction.Y);
			
			var action = new SetMoveDirectionAction 
			{ 
				Direction = fixedDirection, 
				Speed = 10 
			};
			
			// Execute locally (prediction) and send to server
			GameManager.Instance.GameClient.Execute(action, localPlayerId);
		}
	}
}
