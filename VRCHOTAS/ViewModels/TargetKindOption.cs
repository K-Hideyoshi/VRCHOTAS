using VRCHOTAS.Models;

namespace VRCHOTAS.ViewModels;

public sealed class TargetKindOption
{
    public TargetKindOption(string label, MappingTargetKind kind)
    {
        Label = label;
        Kind = kind;
    }

    public string Label { get; }
    public MappingTargetKind Kind { get; }
}
