using Godot;
using System;
using System.Collections.Generic;

namespace Template.Godot.Visuals;

public sealed class NumberRoller
{
    private readonly Label _host;
    private readonly Control _icon;
    private readonly int _padWidth;
    private readonly float _stepTime;
    private readonly float _maxTotal;
    private readonly List<DigitWheel> _wheels = new();
    private int _current;
    private Tween _bounceTween;
    private bool _initialized;
    private bool _wheelsBuilt;
    private int _pendingValue;
    private bool _hasPending;

    public NumberRoller(Label label, Control icon = null, int padWidth = 4,
        float stepTime = 0.08f, float maxTotal = 0.8f)
    {
        _host = label;
        _icon = icon;
        _padWidth = padWidth;
        _stepTime = stepTime;
        _maxTotal = maxTotal;

        _host.Text = "";
        Callable.From(BuildWheels).CallDeferred();
    }

    private void BuildWheels()
    {
        if (_wheelsBuilt) return;
        _wheelsBuilt = true;

        Vector2 hostSize = _host.Size;
        if (hostSize.X < 1 || hostSize.Y < 1)
            hostSize = new Vector2(60 * _padWidth, 30);
        Vector2 wheelSize = new(hostSize.X / _padWidth, hostSize.Y);

        var hbox = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hbox.AddThemeConstantOverride("separation", 0);
        hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _host.AddChild(hbox);

        for (int i = 0; i < _padWidth; i++)
        {
            var wheel = new DigitWheel(_host, wheelSize);
            hbox.AddChild(wheel);
            _wheels.Add(wheel);
        }

        if (_hasPending)
        {
            _hasPending = false;
            SnapTo(_pendingValue);
        }
    }

    public void SetValue(int value)
    {
        if (!_wheelsBuilt)
        {
            _pendingValue = value;
            _hasPending = true;
            _current = value;
            _initialized = true;
            return;
        }
        if (!_initialized)
        {
            _initialized = true;
            _current = value;
            SnapTo(value);
            return;
        }
        if (value == _current) return;

        int direction = Math.Sign(value - _current);
        string oldPad = ToPadded(_current);
        string newPad = ToPadded(value);

        int significant = Math.Max(1, Math.Abs(value).ToString().Length);
        int leading = Math.Max(0, _padWidth - significant);

        for (int i = 0; i < _padWidth; i++)
        {
            int oldDigit = oldPad[i] - '0';
            int newDigit = newPad[i] - '0';
            _wheels[i].SetDim(i < leading);
            if (oldDigit != newDigit)
            {
                int diff = direction > 0
                    ? ((newDigit - oldDigit) + 10) % 10
                    : ((oldDigit - newDigit) + 10) % 10;
                if (diff == 0) continue;
                float duration = Math.Min(diff * _stepTime, _maxTotal);
                _wheels[i].SpinTo(oldDigit, newDigit, duration, direction);
            }
        }

        _current = value;
        Bounce();
    }

    private void SnapTo(int value)
    {
        string pad = ToPadded(value);
        int significant = Math.Max(1, Math.Abs(value).ToString().Length);
        int leading = Math.Max(0, _padWidth - significant);
        for (int i = 0; i < _padWidth; i++)
        {
            _wheels[i].SnapTo(pad[i] - '0');
            _wheels[i].SetDim(i < leading);
        }
    }

    private string ToPadded(int value)
    {
        string s = Math.Abs(value).ToString().PadLeft(_padWidth, '0');
        return s.Length > _padWidth ? s[^_padWidth..] : s;
    }

    private void Bounce()
    {
        if (_icon == null || !GodotObject.IsInstanceValid(_icon)) return;
        if (_icon.PivotOffset == Vector2.Zero && _icon.Size != Vector2.Zero)
            _icon.PivotOffset = _icon.Size / 2f;
        if (_bounceTween != null && GodotObject.IsInstanceValid(_bounceTween) && _bounceTween.IsRunning())
            _bounceTween.Kill();
        _icon.Scale = Vector2.One;
        _bounceTween = _icon.CreateTween();
        _bounceTween.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _bounceTween.TweenProperty(_icon, "scale", new Vector2(1.25f, 1.25f), 0.09f);
        _bounceTween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _bounceTween.TweenProperty(_icon, "scale", Vector2.One, 0.18f);
    }
}

public sealed partial class DigitWheel : Control
{
    private const float DimAlpha = 0.3f;
    private const int StripDigits = 21;

    private readonly Label _strip;
    private readonly float _digitHeight;
    private readonly float _vOffset;
    private bool _isDim;
    private Tween _dimTween;
    private Tween _spinTween;

    public DigitWheel(Label themeSource, Vector2 wheelSize)
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;

        var font = themeSource.GetThemeFont("font");
        int fontSize = themeSource.GetThemeFontSize("font_size");
        Color fontColor = themeSource.GetThemeColor("font_color");
        Color outlineColor = themeSource.HasThemeColor("font_outline_color")
            ? themeSource.GetThemeColor("font_outline_color") : new Color(0, 0, 0, 1);
        int outlineSize = themeSource.HasThemeConstant("outline_size")
            ? themeSource.GetThemeConstant("outline_size") : 0;

        var sb = new System.Text.StringBuilder(StripDigits * 2);
        for (int i = 0; i < StripDigits; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append((char)('0' + (i % 10)));
        }

        _strip = new Label
        {
            Text = sb.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _strip.AddThemeFontOverride("font", font);
        _strip.AddThemeFontSizeOverride("font_size", fontSize);
        _strip.AddThemeColorOverride("font_color", fontColor);
        _strip.AddThemeColorOverride("font_outline_color", outlineColor);
        _strip.AddThemeConstantOverride("outline_size", outlineSize);
        _strip.AddThemeConstantOverride("line_spacing", 0);
        AddChild(_strip);

        _digitHeight = font.GetHeight(fontSize);
        _vOffset = (wheelSize.Y - _digitHeight) * 0.5f;
        CustomMinimumSize = wheelSize;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        _strip.CustomMinimumSize = new Vector2(wheelSize.X, _digitHeight * StripDigits);
    }

    public void SnapTo(int digit)
    {
        if (_spinTween != null && GodotObject.IsInstanceValid(_spinTween) && _spinTween.IsRunning())
            _spinTween.Kill();
        SetStripPos(digit);
    }

    public void SpinTo(int from, int to, float duration, int direction)
    {
        if (_spinTween != null && GodotObject.IsInstanceValid(_spinTween) && _spinTween.IsRunning())
            _spinTween.Kill();
        if (from == to) return;

        float start, end;
        if (direction >= 0)
        {
            int diff = ((to - from) + 10) % 10;
            start = from;
            end = from + diff;
        }
        else
        {
            int diff = ((from - to) + 10) % 10;
            start = to + diff;
            end = to;
        }
        SetStripPos(start);
        _spinTween = CreateTween();
        _spinTween.SetTrans(Tween.TransitionType.Linear);
        _spinTween.TweenMethod(Callable.From<float>(SetStripPos), start, end, duration);
        _spinTween.TweenCallback(Callable.From(() => SetStripPos(to)));
    }

    private void SetStripPos(float pos)
    {
        var p = _strip.Position;
        _strip.Position = new Vector2(p.X, -pos * _digitHeight + _vOffset);
    }

    public void SetDim(bool dim)
    {
        if (dim == _isDim) return;
        _isDim = dim;
        if (_dimTween != null && GodotObject.IsInstanceValid(_dimTween) && _dimTween.IsRunning())
            _dimTween.Kill();
        _dimTween = CreateTween();
        _dimTween.TweenProperty(_strip, "modulate:a", dim ? DimAlpha : 1f, 0.15f);
    }
}
