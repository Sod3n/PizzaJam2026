using FluentAssertions;
using Template.CodeGen.Generators;
using Template.CodeGen.Parsing;
using Xunit;

namespace Template.CodeGen.Tests;

public class IntegrationTests
{
    private const string FullCowTscn = @"
[gd_scene format=3 uid=""uid://cow_test""]

[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkEntity.cs"" id=""1_entity""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkComponent.cs"" id=""2_comp""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkPhysicsBody.cs"" id=""3_phys""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkNavAgent.cs"" id=""4_nav""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkState.cs"" id=""5_state""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkSkin.cs"" id=""6_skin""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkVisualBinding.cs"" id=""7_bind""]

[node name=""Cow"" type=""Node3D""]
script = ExtResource(""1_entity"")
EntityName = ""Cow""
StableIdSeed = ""cow-stable-seed-001""

[node name=""CowComponent"" type=""Node"" parent="".""]
script = ExtResource(""2_comp"")
ComponentName = ""CowComponent""
FieldNames = PackedStringArray(""Exhaust"", ""MaxExhaust"", ""IsMilking"", ""HouseId"", ""SpawnPosition"", ""FollowingPlayer"")
FieldTypes = PackedStringArray(""int"", ""int"", ""bool"", ""Entity"", ""Vector2"", ""Entity"")

[node name=""Physics"" type=""Node"" parent="".""]
script = ExtResource(""3_phys"")
BodyType = ""CharacterBody2D""
ShapeType = ""Circle""
ShapeRadius = 0.5
CollisionLayer = 1
CollisionMask = 3

[node name=""NavAgent"" type=""Node"" parent="".""]
script = ExtResource(""4_nav"")
MaxSpeed = 10
TargetDesiredDistance = 2.0
PathDesiredDistance = 1.0
Radius = 0.5

[node name=""State"" type=""Node"" parent="".""]
script = ExtResource(""5_state"")

[node name=""Skin"" type=""Node"" parent="".""]
script = ExtResource(""6_skin"")

[node name=""CapacityBinding"" type=""Node"" parent="".""]
script = ExtResource(""7_bind"")
ComponentField = ""Exhaust""
TargetNodePath = ""Capacity""
TargetProperty = ""text""
";

    [Fact]
    public void FullPipeline_CowEntity_GeneratesAllOutputs()
    {
        var parser = new TscnParser();
        var entity = parser.Parse(FullCowTscn, "Cow.tscn");
        entity.Should().NotBeNull();
        entity!.EntityName.Should().Be("Cow");

        var componentCode = ComponentGenerator.Generate(entity.Components[0]);
        componentCode.Should().Contain("public struct CowComponent : IComponent");
        componentCode.Should().Contain("public int Exhaust;");
        componentCode.Should().Contain("public Entity HouseId;");

        var defCode = DefinitionGenerator.Generate(entity);
        defCode.Should().Contain("public static partial class CowDefinition");
        defCode.Should().Contain("typeof(CowComponent)");
        defCode.Should().Contain("typeof(CharacterBody2D)");
        defCode.Should().Contain("typeof(NavigationAgent2D)");

        var vmCode = ViewModelGenerator.Generate(entity);
        vmCode.Should().Contain("public partial class CowViewModel : EntityViewModel");
        vmCode.Should().Contain("CowDefinitionModel");
        vmCode.Should().Contain("SkinComponentModel");

        var vizCode = VisualizerGenerator.Generate(new List<Models.EntityDescriptor> { entity });
        vizCode.Should().Contain("public partial class EntityVisualizer");
        vizCode.Should().Contain("CowPrefab_Generated");
        vizCode.Should().Contain("ObservableList<CowComponent");
    }

    [Fact]
    public void FullPipeline_MinimalWallEntity()
    {
        var tscn = @"
[gd_scene format=3]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkEntity.cs"" id=""1_e""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkComponent.cs"" id=""2_c""]
[node name=""Wall"" type=""Node3D""]
script = ExtResource(""1_e"")
EntityName = ""Wall""
StableIdSeed = ""wall-seed""
[node name=""WallComponent"" type=""Node"" parent="".""]
script = ExtResource(""2_c"")
ComponentName = ""WallComponent""
";
        var parser = new TscnParser();
        var entity = parser.Parse(tscn);
        entity.Should().NotBeNull();

        var compCode = ComponentGenerator.Generate(entity!.Components[0]);
        compCode.Should().Contain("public struct WallComponent : IComponent");

        var defCode = DefinitionGenerator.Generate(entity);
        defCode.Should().Contain("public static partial class WallDefinition");
        defCode.Should().NotContain("CharacterBody2D");
    }

    [Fact]
    public void FullPipeline_StableIds_AreDeterministic()
    {
        var parser = new TscnParser();
        var e1 = parser.Parse(FullCowTscn);
        var e2 = parser.Parse(FullCowTscn);
        e1!.Components[0].StableId.Should().Be(e2!.Components[0].StableId);
    }

    [Fact]
    public void FullPipeline_MultipleEntities_VisualizerRegistersAll()
    {
        var parser = new TscnParser();
        var cow = parser.Parse(FullCowTscn)!;
        var wallTscn = @"
[gd_scene format=3]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkEntity.cs"" id=""1_e""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkComponent.cs"" id=""2_c""]
[node name=""Wall"" type=""Node3D""]
script = ExtResource(""1_e"")
EntityName = ""Wall""
StableIdSeed = ""wall-seed""
[node name=""WallComponent"" type=""Node"" parent="".""]
script = ExtResource(""2_c"")
ComponentName = ""WallComponent""
";
        var wall = parser.Parse(wallTscn)!;

        var vizCode = VisualizerGenerator.Generate(new List<Models.EntityDescriptor> { cow, wall });
        vizCode.Should().Contain("CowPrefab_Generated");
        vizCode.Should().Contain("WallPrefab_Generated");
        vizCode.Should().Contain("partial void OnSpawnCow");
        vizCode.Should().Contain("partial void OnSpawnWall");
    }
}
