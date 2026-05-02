using Godot;
using Template.Shared.Components;

namespace Template.Godot.GameResources;

[GlobalClass]
public partial class LandTypeIconEntry : Resource
{
    [Export] public LandType LandType { get; set; }
    [Export] public Texture2D Icon { get; set; }
}
