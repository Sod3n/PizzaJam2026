using Godot;
using Template.Godot.Core;
using Template.Shared.Components;
using Template.Shared.GameData;

namespace Template.Godot.Visuals;

public partial class DayCycleView : Node
{
    [Export] public NodePath SunPath;
    [Export] public NodePath EnvPath;

    [Export] public float SunYawStart = 40f;
    [Export] public float SunYawEnd = -40f;

    // Curve shape: morning lerps to midday over [0, MorningEnd], midday holds across the
    // middle of the day, then evening lerps in over [EveningStart, 1]. Keeps the bulk of
    // the day looking like daylight instead of constantly drifting toward sunset.
    [Export] public float MorningEnd = 0.2f;
    [Export] public float EveningStart = 0.85f;

    [Export] public Color AmbientMorning = new(0.78f, 0.78f, 0.80f); // near-neutral, slight cool
    [Export] public Color AmbientMidday = new(0.78f, 0.85f, 0.95f);  // soft sky blue
    [Export] public Color AmbientEvening = new(0.90f, 0.82f, 0.55f); // muted gold

    [Export] public Color SunMorning = new(1.0f, 0.90f, 0.78f);
    [Export] public Color SunMidday = new(1.0f, 0.98f, 0.94f);
    [Export] public Color SunEvening = new(1.0f, 0.85f, 0.55f);

    [Export] public float SunEnergyMorning = 0.6f;
    [Export] public float SunEnergyMidday = 1.2f;
    [Export] public float SunEnergyEvening = 0.5f;

    [Export] public float AmbientEnergyMorning = 0.7f;
    [Export] public float AmbientEnergyMidday = 1.1f;
    [Export] public float AmbientEnergyEvening = 0.6f;

    private DirectionalLight3D _sun;
    private global::Godot.Environment _env;

    private float _initialPitch;
    private float _initialRoll;
    private bool _capturedInitial;

    private float _localSeconds;
    private int _lastSeenDay = int.MinValue;

    public override void _Ready()
    {
        _sun = GetNodeOrNull<DirectionalLight3D>(SunPath ?? "../DirectionalLight3D");
        var envHost = GetNodeOrNull<WorldEnvironment>(EnvPath ?? "../WorldEnvironment");
        _env = envHost?.Environment;
    }

    public override void _Process(double delta)
    {
        if (_sun == null || _env == null) return;

        if (!_capturedInitial)
        {
            var euler = _sun.RotationDegrees;
            _initialPitch = euler.X;
            _initialRoll = euler.Z;
            _capturedInitial = true;
        }

        int currentDay = _lastSeenDay;
        var state = GameManager.Instance?.Game?.State;
        if (state != null)
        {
            foreach (var ge in state.Filter<GlobalResourcesComponent>())
            {
                currentDay = state.GetComponent<GlobalResourcesComponent>(ge).DayCounter;
                break;
            }
        }
        if (currentDay != _lastSeenDay)
        {
            _localSeconds = 0f;
            _lastSeenDay = currentDay;
        }
        _localSeconds += (float)delta;

        float dayLenSeconds = Balance.Day.LengthTicks / (float)Balance.TickRate;
        if (dayLenSeconds <= 0f) dayLenSeconds = 1f;
        float t = Mathf.Clamp(_localSeconds / dayLenSeconds, 0f, 1f);

        float yaw = Mathf.Lerp(SunYawStart, SunYawEnd, t);
        _sun.RotationDegrees = new Vector3(_initialPitch, yaw, _initialRoll);

        // Three-phase blend: warm-up → midday hold → cool-down to evening.
        float morningT = MorningEnd > 0f ? Mathf.Clamp(t / MorningEnd, 0f, 1f) : 1f;
        float eveningT = EveningStart < 1f ? Mathf.Clamp((t - EveningStart) / (1f - EveningStart), 0f, 1f) : 0f;

        Color ambient = AmbientMorning.Lerp(AmbientMidday, morningT).Lerp(AmbientEvening, eveningT);
        Color sunColor = SunMorning.Lerp(SunMidday, morningT).Lerp(SunEvening, eveningT);
        float sunEnergy = Mathf.Lerp(Mathf.Lerp(SunEnergyMorning, SunEnergyMidday, morningT), SunEnergyEvening, eveningT);
        float ambientEnergy = Mathf.Lerp(Mathf.Lerp(AmbientEnergyMorning, AmbientEnergyMidday, morningT), AmbientEnergyEvening, eveningT);

        _sun.LightColor = sunColor;
        _sun.LightEnergy = sunEnergy;
        _env.AmbientLightColor = ambient;
        _env.AmbientLightEnergy = ambientEnergy;
    }
}
