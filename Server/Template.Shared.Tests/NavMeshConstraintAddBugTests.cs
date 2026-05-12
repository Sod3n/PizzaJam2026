using System;
using System.Collections.Generic;
using Deterministic.GameFramework.Common;
using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using Deterministic.GameFramework.Navigation2D.Components;
using Deterministic.GameFramework.Physics2D.Components;
using Deterministic.GameFramework.TwoD;
using Deterministic.GameFramework.Types;
using FluentAssertions;
using Template.Shared.Actions;
using Template.Shared.Components;
using Template.Shared.Definitions;
using Template.Shared.Factories;
using Xunit;
using Xunit.Abstractions;

namespace Template.Shared.Tests;

public class NavMeshConstraintAddBugTests
{
    private readonly ITestOutputHelper _output;

    public NavMeshConstraintAddBugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PlayerDefinition_Create_AttachesNavMeshConstraint()
    {
        var game = TemplateGameFactory.CreateGame();
        var state = game.Simulation.State;

        // Drive a player creation through the dispatcher, like the live path does.
        var worldEntityFound = Entity.Null;
        foreach (var e in state.Filter<World>())
        {
            worldEntityFound = e;
            break;
        }
        worldEntityFound.Should().NotBe(Entity.Null, "GameplayScene should have created the world entity");

        game.Loop.ScheduleOnTick(game.Loop.CurrentTick + 1,
            new AddPlayerAction(Guid.NewGuid()), worldEntityFound);

        // Tick a few times so the action and any post-processing runs.
        for (int i = 0; i < 5; i++) game.Loop.RunSingleTick();

        Entity playerEntity = Entity.Null;
        foreach (var e in state.Filter<PlayerEntity>())
        {
            playerEntity = e;
            break;
        }
        playerEntity.Should().NotBe(Entity.Null, "AddPlayerAction should have created a player");

        _output.WriteLine($"player entity id = {playerEntity.Id}");
        _output.WriteLine($"HasComponent<CharacterBody2D> = {state.HasComponent<CharacterBody2D>(playerEntity)}");
        _output.WriteLine($"HasComponent<NavMeshConstraint> = {state.HasComponent<NavMeshConstraint>(playerEntity)}");
        _output.WriteLine($"ComponentId<NavMeshConstraint>.IntId = {ComponentId<NavMeshConstraint>.IntId}");
        _output.WriteLine($"ComponentId<CharacterBody2D>.IntId = {ComponentId<CharacterBody2D>.IntId}");

        state.HasComponent<CharacterBody2D>(playerEntity).Should().BeTrue();
        state.HasComponent<NavMeshConstraint>(playerEntity).Should().BeTrue(
            "NavMeshConstraint is added in PlayerDefinition.Create and must survive to post-tick");
    }
}
