using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Template.Godot.Visuals;

public partial class HintPopup : Control
{
    private const string DimColor = "26262610";
    private const string CharSet =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,!?-—";

    private IconRoller _iconRoller;
    private RichTextLabel _filler;
    private Control _wheelsContainer;
    private RichTextLabel _hintText;

    private char[] _fillerChars;
    private int _cols;
    private int _rows;
    private float _charW;
    private float _lineH;
    private Font _font;
    private int _fontSize;
    private readonly System.Random _rng = new();

    private readonly Dictionary<int, CharWheel> _wheels = new();

    public override void _Ready()
    {
        _hintText = GetNodeOrNull<RichTextLabel>("%HintText");
        if (_hintText != null)
        {
            _hintText.Text = "";
            BuildDisplay();
        }

        var hintIcon = GetNodeOrNull<TextureRect>("%HintIcon");
        if (hintIcon != null) _iconRoller = new IconRoller(hintIcon);
    }

    private void BuildDisplay()
    {
        _filler = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
            Theme = _hintText.Theme,
            ThemeTypeVariation = _hintText.ThemeTypeVariation,
            Position = _hintText.Position,
            Size = _hintText.Size,
            ClipContents = true,
        };
        if (_hintText.HasThemeFontSizeOverride("normal_font_size"))
            _filler.AddThemeFontSizeOverride("normal_font_size",
                _hintText.GetThemeFontSize("normal_font_size"));
        AddChild(_filler);
        MoveChild(_filler, _hintText.GetIndex());

        _font = _filler.GetThemeFont("normal_font") ?? _filler.GetThemeDefaultFont() ?? ThemeDB.FallbackFont;
        _fontSize = _filler.GetThemeFontSize("normal_font_size");
        if (_fontSize <= 0) _fontSize = _filler.GetThemeDefaultFontSize();
        if (_fontSize <= 0) _fontSize = 16;
        if (_font == null)
        {
            GD.PrintErr("[HintPopup] filler font resolution failed");
            return;
        }

        _charW = _font.GetStringSize("M", HorizontalAlignment.Left, -1, _fontSize).X;
        _lineH = _font.GetHeight(_fontSize);
        _cols = Mathf.Max(1, (int)(_filler.Size.X / _charW));
        _rows = Mathf.Max(1, (int)(_filler.Size.Y / _lineH));

        _fillerChars = new char[_cols * _rows];
        for (int i = 0; i < _fillerChars.Length; i++)
            _fillerChars[i] = CharSet[_rng.Next(26)];
        RenderFiller();

        _wheelsContainer = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Theme = _hintText.Theme,
            Position = _filler.Position,
            Size = _filler.Size,
            ClipContents = true,
        };
        AddChild(_wheelsContainer);
        MoveChild(_wheelsContainer, _filler.GetIndex() + 1);
    }

    private void RenderFiller()
    {
        if (_filler == null || _fillerChars == null) return;
        var sb = new StringBuilder();
        for (int r = 0; r < _rows; r++)
        {
            if (r > 0) sb.Append('\n');
            for (int c = 0; c < _cols; c++)
            {
                int idx = r * _cols + c;
                bool covered = _wheels.ContainsKey(idx);
                sb.Append("[color=#")
                  .Append(covered ? "00000000" : DimColor)
                  .Append(']');
                AppendEscaped(sb, _fillerChars[idx]);
                sb.Append("[/color]");
            }
        }
        _filler.Text = sb.ToString();
    }

    public void SetText(string text)
    {
        if (_wheelsContainer == null) return;
        text ??= "";

        var layout = LayoutWords(text);

        var toRemove = new List<int>();
        var toCreate = new List<(int idx, char target)>();

        for (int idx = 0; idx < _fillerChars.Length; idx++)
        {
            if (layout.TryGetValue(idx, out var target))
            {
                if (_wheels.TryGetValue(idx, out var wheel))
                    wheel.RollTo(target);
                else
                    toCreate.Add((idx, target));
            }
            else if (_wheels.ContainsKey(idx))
            {
                toRemove.Add(idx);
            }
        }

        foreach (int idx in toRemove)
        {
            _wheels[idx].QueueFree();
            _wheels.Remove(idx);
        }

        foreach (var (idx, target) in toCreate)
        {
            int row = idx / _cols;
            int col = idx % _cols;
            var w = new CharWheel(_font, _fontSize, new Vector2(_charW, _lineH),
                _hintText.ThemeTypeVariation);
            w.Position = new Vector2(col * _charW, row * _lineH);
            _wheelsContainer.AddChild(w);
            w.SnapTo(_fillerChars[idx]);
            w.RollTo(target);
            _wheels[idx] = w;
        }

        RenderFiller();
    }

    private Dictionary<int, char> LayoutWords(string text)
    {
        var map = new Dictionary<int, char>();
        var words = text.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
        int row = 0;
        int col = 0;

        foreach (var word in words)
        {
            if (word.Length > _cols)
            {
                if (col != 0) { row++; col = 0; }
                foreach (var ch in word)
                {
                    if (row >= _rows) return map;
                    if (col >= _cols) { row++; col = 0; if (row >= _rows) return map; }
                    map[row * _cols + col] = ch;
                    col++;
                }
            }
            else
            {
                if (col != 0 && col + word.Length > _cols) { row++; col = 0; }
                if (row >= _rows) return map;
                foreach (var ch in word)
                {
                    map[row * _cols + col] = ch;
                    col++;
                }
            }
            col++;
            if (col >= _cols) { row++; col = 0; }
            if (row >= _rows) return map;
        }
        return map;
    }

    public void SetIcon(Texture2D icon) => _iconRoller?.SetTexture(icon);

    public static int IndexInCharSet(char c) => CharSet.IndexOf(c);
    public static int CharSetLength => CharSet.Length;
    public static char CharAt(int idx) => CharSet[((idx % CharSet.Length) + CharSet.Length) % CharSet.Length];

    private static void AppendEscaped(StringBuilder sb, char c)
    {
        if (c == '[') sb.Append("[lb]");
        else if (c == ']') sb.Append("[rb]");
        else sb.Append(c);
    }
}

public sealed partial class CharWheel : Control
{
    private const int FakeSteps = 8;
    private const float StepTime = 0.12f;

    private readonly Label _strip;
    private readonly Font _font;
    private readonly int _fontSize;
    private readonly System.Random _rng = new();
    private char _currentChar;
    private Tween _tween;
    private float _lineH;

    public CharWheel(Font font, int fontSize, Vector2 size, string themeTypeVariation)
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = size;
        Size = size;

        _font = font;
        _fontSize = fontSize;
        _lineH = font.GetHeight(fontSize);

        _strip = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            ThemeTypeVariation = themeTypeVariation,
        };
        _strip.AddThemeFontOverride("font", font);
        _strip.AddThemeFontSizeOverride("font_size", fontSize);
        _strip.AddThemeConstantOverride("line_spacing", 0);
        AddChild(_strip);
    }

    public void SnapTo(char c)
    {
        _currentChar = c;
        _strip.Text = c.ToString();
        _strip.Position = Vector2.Zero;
    }

    public void RollTo(char target)
    {
        if (target == _currentChar) return;

        if (_tween != null && GodotObject.IsInstanceValid(_tween) && _tween.IsRunning())
            _tween.Kill();

        var sb = new StringBuilder();
        sb.Append(_currentChar);
        for (int i = 0; i < FakeSteps; i++)
        {
            sb.Append('\n');
            sb.Append(HintPopup.CharAt(_rng.Next(HintPopup.CharSetLength)));
        }
        sb.Append('\n');
        sb.Append(target);
        _strip.Text = sb.ToString();

        int totalSteps = FakeSteps + 1;
        float duration = totalSteps * StepTime;

        _strip.Position = Vector2.Zero;
        _tween = CreateTween();
        _tween.SetTrans(Tween.TransitionType.Linear);
        _tween.TweenProperty(_strip, "position",
            new Vector2(0, -totalSteps * _lineH), duration);

        char finalChar = target;
        _tween.TweenCallback(Callable.From(() =>
        {
            _currentChar = finalChar;
            _strip.Text = finalChar.ToString();
            _strip.Position = Vector2.Zero;
        }));
    }
}
