using Godot;

namespace Template.Godot.Visuals;

public sealed class TextRoller
{
    private readonly RichTextLabel _host;
    private readonly float _duration;
    private RichTextLabel _current;
    private bool _wrapped;
    private string _pending;
    private bool _hasPending;

    public TextRoller(RichTextLabel host, float duration = 0.35f)
    {
        _host = host;
        _duration = duration;
        Callable.From(Setup).CallDeferred();
    }

    private void Setup()
    {
        if (_wrapped) return;
        _wrapped = true;
        _host.ClipContents = true;
        var initial = _host.Text;
        _host.Text = "";
        _current = MakeChild();
        _current.Text = initial;
        if (_hasPending) { var p = _pending; _pending = null; _hasPending = false; SetText(p); }
    }

    private RichTextLabel MakeChild()
    {
        var child = new RichTextLabel
        {
            BbcodeEnabled = _host.BbcodeEnabled,
            FitContent = _host.FitContent,
            AutowrapMode = _host.AutowrapMode,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ThemeTypeVariation = _host.ThemeTypeVariation,
            Size = _host.Size,
        };
        if (_host.HasThemeFontSizeOverride("normal_font_size"))
            child.AddThemeFontSizeOverride("normal_font_size", _host.GetThemeFontSize("normal_font_size"));
        _host.AddChild(child);
        return child;
    }

    public void SetText(string text)
    {
        if (!_wrapped) { _pending = text; _hasPending = true; return; }
        if (_current == null) return;
        if (_current.Text == text) return;
        if (string.IsNullOrEmpty(_current.Text))
        {
            _current.Text = text;
            return;
        }
        var outgoing = _current;
        var incoming = MakeChild();
        incoming.Text = text;
        incoming.Position = new Vector2(0, _host.Size.Y);
        _current = incoming;

        var tween = _host.CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.SetParallel(true);
        tween.TweenProperty(incoming, "position", Vector2.Zero, _duration);
        tween.TweenProperty(outgoing, "position", new Vector2(0, -_host.Size.Y), _duration);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(outgoing)) outgoing.QueueFree();
        }));
    }
}

public sealed class IconRoller
{
    private readonly TextureRect _host;
    private readonly float _duration;
    private TextureRect _current;
    private bool _wrapped;
    private Texture2D _pending;
    private bool _hasPending;

    public IconRoller(TextureRect host, float duration = 0.35f)
    {
        _host = host;
        _duration = duration;
        Callable.From(Setup).CallDeferred();
    }

    private void Setup()
    {
        if (_wrapped) return;
        _wrapped = true;
        _host.ClipContents = true;
        var initial = _host.Texture;
        _host.Texture = null;
        _current = MakeChild();
        _current.Texture = initial;
        if (_hasPending) { var p = _pending; _pending = null; _hasPending = false; SetTexture(p); }
    }

    private TextureRect MakeChild()
    {
        var child = new TextureRect
        {
            ExpandMode = _host.ExpandMode,
            StretchMode = _host.StretchMode,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = _host.Size,
        };
        _host.AddChild(child);
        return child;
    }

    public void SetTexture(Texture2D tex)
    {
        if (!_wrapped) { _pending = tex; _hasPending = true; return; }
        if (_current == null) return;
        if (_current.Texture == tex) return;
        if (_current.Texture == null)
        {
            _current.Texture = tex;
            return;
        }
        var outgoing = _current;
        var incoming = MakeChild();
        incoming.Texture = tex;
        incoming.Position = new Vector2(0, _host.Size.Y);
        _current = incoming;

        var tween = _host.CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.SetParallel(true);
        tween.TweenProperty(incoming, "position", Vector2.Zero, _duration);
        tween.TweenProperty(outgoing, "position", new Vector2(0, -_host.Size.Y), _duration);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(outgoing)) outgoing.QueueFree();
        }));
    }
}
