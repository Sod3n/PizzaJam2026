# Template.Godot Client

This is a **Godot 4.x (C#)** client implementation for the Deterministic Game Framework.

## Prerequisites

1.  **Godot Engine 4.x** (.NET / Mono Version) installed.
2.  **.NET 8.0 SDK** installed.

## Setup

1.  Open Godot Engine.
2.  Click **Import**.
3.  Navigate to this folder (`Client/Template.Godot`) and select the `project.godot` file.
4.  Click **Import & Edit**.
5.  Once opened, click **Build** (top right) to ensure the C# solution is compiled.

## Running

1.  Make sure the **Server** is running (`dotnet run` in `Server/Template.Server`).
2.  In Godot, press **F5** (Play Project).
3.  The client will automatically:
    *   Connect to `127.0.0.1:9050`.
    *   Queue for a match.
    *   Wait for the server to assign a match.
    *   Start the game loop and spawn a visualizer.

## Controls

*   **Arrow Keys**: Move the character.

## Structure

*   **Scenes/Main.tscn**: The entry point scene containing the Game Loop and Managers.
*   **Scripts/Core/GameManager.cs**: Handles Network connection, Matchmaking, and the Deterministic Game Loop.
*   **Scripts/Input/InputManager.cs**: Polls Godot Input and sends deterministic actions to the server.
*   **Scripts/Visuals/EntityVisualizer.cs**: Interpolates and renders ECS entities using Godot Nodes.

## Customization

*   **Prefabs**: You can assign custom `.tscn` prefabs to the `PlayerPrefab` and `CoinPrefab` properties on the `EntityVisualizer` node in `Main.tscn` to change how entities look.
