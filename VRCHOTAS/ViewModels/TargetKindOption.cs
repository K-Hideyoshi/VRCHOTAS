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

public sealed class AxisTargetOption
{
    public AxisTargetOption(string label, VirtualAxisTarget target)
    {
        Label = label;
        Target = target;
    }

    public string Label { get; }
    public VirtualAxisTarget Target { get; }
}

public sealed class ButtonTargetOption
{
    public ButtonTargetOption(string label, VirtualButtonTarget target)
    {
        Label = label;
        Target = target;
    }

    public string Label { get; }
    public VirtualButtonTarget Target { get; }
}

public sealed class AxisActionTargetOption
{
    public AxisActionTargetOption(string label, ControllerPoseActionTarget target)
    {
        Label = label;
        Target = target;
    }

    public string Label { get; }
    public ControllerPoseActionTarget Target { get; }
}

public sealed class ControllerPoseTargetOption
{
    public ControllerPoseTargetOption(string label, ControllerPoseTarget target)
    {
        Label = label;
        Target = target;
    }

    public string Label { get; }
    public ControllerPoseTarget Target { get; }
}

public sealed class ControllerOutputModeOption
{
    public ControllerOutputModeOption(string label, ControllerOutputMode mode)
    {
        Label = label;
        Mode = mode;
    }

    public string Label { get; }
    public ControllerOutputMode Mode { get; }
}
