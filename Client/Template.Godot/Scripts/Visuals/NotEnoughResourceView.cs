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

    public void Setup(string resourceKey)
    {
        if (_sprite == null) return;

        // Map resource key to icon filename
        var iconName = resourceKey switch
        {
            "food" => "grass",  // generic food icon
            _ => resourceKey    // milk, coins, houses etc.
        };

        var texture = GD.Load<Texture2D>($"res://sprites/export/icons/{iconName}.png");
        if (texture == null)
        {
            // Fallback — try loading from resources folder
            texture = GD.Load<Texture2D>($"res://sprites/resources/{iconName}.png");
        }

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
