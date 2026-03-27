using Deterministic.GameFramework.DAR;
using Deterministic.GameFramework.ECS;
using R3;

namespace Template.Godot.Visuals;

public partial class CowViewModel
{
    public ReadOnlyReactiveProperty<string> Capacity { get; private set; } = null!;

    partial void OnInitialize(Context context)
    {
        Capacity = Cow.Cow.MaxExhaust
            .CombineLatest(Cow.Cow.Exhaust, (max, current) => $"[{max - current}/{max}]")
            .ToReadOnlyReactiveProperty();
        Disposables.Add(Capacity);
    }
}
