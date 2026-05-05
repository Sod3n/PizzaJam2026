using Godot;
using Template.Shared;

namespace Template.Godot.Visuals;

public partial class NotEnoughResourceView : Node3D
{
    [Export] public float RiseDistance = 1.5f;
    [Export] public float Duration = 1.0f;

    private AnimatedSprite3D _sprite;

    public override void _Ready()
    {
        _sprite = GetNode<AnimatedSprite3D>("AnimatedSprite3D");
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> IconPaths = new()
    {
        { "grass", "res://sprites/export/icons/Grass_/1.png" },
        { "carrot", "res://sprites/export/icons/Carrot_/1.png" },
        { "apple", "res://sprites/export/icons/Apply_/1.png" },
        { "mushroom", "res://sprites/export/icons/Mashroom/1.png" },
        { "milk", "res://sprites/export/icons/Milky_/1.png" },
        { "carrot_milkshake", "res://sprites/export/icons/Milky_/1.png" },
        { "vitamin_mix", "res://sprites/export/icons/Milky_/1.png" },
        { "purple_potion", "res://sprites/export/icons/Milky_/1.png" },
        { "cow_tired", "res://sprites/export/icons/Milky_/1.png" },
        { "coins", "res://sprites/export/icons/Money_/1.png" },
        { "food", "res://sprites/export/icons/Grass_/1.png" },
        { "houses", "res://sprites/export/homes/A_bar.png" },
    };

    public void Setup(string resourceKey)
    {
        if (_sprite == null) return;

        Texture2D texture = null;
        if (IconPaths.TryGetValue(resourceKey, out var path))
            texture = GD.Load<Texture2D>(path);

        if (texture != null)
        {
            var frames = new SpriteFrames();
            frames.AddAnimation(resourceKey);
            frames.AddFrame(resourceKey, texture);
            frames.SetAnimationLoop(resourceKey, false);
            _sprite.SpriteFrames = frames;
            _sprite.Animation = resourceKey;
            _sprite.Frame = 0;
        }

        Animate();
    }

    private void Animate()
    {
        var tween = CreateTween();
        tween.SetParallel(true);

        // Rise up
        var targetPos = Position + new Vector3(0, RiseDistance, 0);
        tween.TweenProperty(this, "position", targetPos, Duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);

        // Fade out (start fading after 40% of duration)
        tween.TweenProperty(_sprite, "modulate:a", 0f, Duration * 0.6f)
            .SetDelay(Duration * 0.4f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);

        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
