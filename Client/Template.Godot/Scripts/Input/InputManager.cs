using Godot;
using System;
using Template.Godot.Core;
using Template.Shared.Components;
using Template.Shared.Features.Movement;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Reactive;

namespace Template.Godot.Input;

public partial class InputManager : Node
{
	[Export] public Core.GameManager GameManager;

	private int _localPlayerEntityId = 0;
	private IDisposable _subscription;
	private Vector2 _lastDirection = new Vector2(float.MaxValue, float.MaxValue);

	public override void _Process(double delta)
	{
		if (GameManager == null || !GameManager.IsGameRunning) return;

		// Initialize subscription if needed (once game is running and client is ready)
		if (_subscription == null)
		{
			var client = GameManager.GameClient;
			if (client != null && client.Reactive != null)
			{
				_subscription = client.Reactive.ObservableCollection<PlayerEntity>()
					.Subscribe(OnPlayerAdded, OnPlayerRemoved);
			}
		}

		if (_localPlayerEntityId == 0) return;

		// 2. Poll Input
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
			GameManager.GameClient.Execute(action, _localPlayerEntityId);
		}
	}

	private void OnPlayerAdded(Entity entity)
	{
		var client = GameManager.GameClient;
		var state = GameManager.Game.State;
		
		if (!state.HasComponent<PlayerEntity>(entity)) return;
		
		ref var player = ref state.GetComponent<PlayerEntity>(entity);
		
		// Compare IDs
		if (player.UserId.ToString() == client.PlayerId.ToString())
		{
			_localPlayerEntityId = entity.Id;
		}
	}

	private void OnPlayerRemoved(Entity entity)
	{
		if (entity.Id == _localPlayerEntityId)
		{
			_localPlayerEntityId = 0;
		}
	}

	public override void _ExitTree()
	{
		_subscription?.Dispose();
		_subscription = null;
	}
}
