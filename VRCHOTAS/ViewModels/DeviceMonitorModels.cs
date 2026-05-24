using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCHOTAS.Models;

namespace VRCHOTAS.ViewModels;

public sealed class DeviceMonitorGroup : ObservableObject
{
    private string _deviceId = string.Empty;
    private string _deviceName = string.Empty;
    private bool _isConnected;
    private bool _hasXAxis;
    private bool _hasYAxis;
    private double _xAxisValue;
    private double _yAxisValue;

    public string DeviceId
    {
        get => _deviceId;
        set => SetProperty(ref _deviceId, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        set => SetProperty(ref _deviceName, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    public ObservableCollection<AxisMonitorItem> Axes { get; } = new();
    public ObservableCollection<ButtonMonitorItem> Buttons { get; } = new();
    public ObservableCollection<PovMonitorItem> Povs { get; } = new();

    public bool HasPositionAxes => _hasXAxis || _hasYAxis;
    public bool HasAnyAxes => HasPositionAxes || HasAxes;
    public bool HasAxes => Axes.Count > 0;
    public bool HasButtons => Buttons.Count > 0;
    public bool HasPovs => Povs.Count > 0;
    public bool HasPhysicalInputs => HasPositionAxes || HasAxes || HasButtons || HasPovs;
    public double XAxisValue => _xAxisValue;
    public double YAxisValue => _yAxisValue;
    public string PositionDisplay => $"X: {XAxisValue:F2}    Y: {YAxisValue:F2}";
    public double PositionIndicatorLeft => ((XAxisValue + 1.0) * 0.5) * 88.0;
    public double PositionIndicatorTop => ((YAxisValue + 1.0) * 0.5) * 88.0;

    public void UpdateFrom(JoystickDeviceState state)
    {
        var hadPositionAxes = HasPositionAxes;
        var hadAxes = HasAxes;
        var hadButtons = HasButtons;
        var hadPovs = HasPovs;
        var hadPhysicalInputs = HasPhysicalInputs;
        var previousXAxisValue = _xAxisValue;
        var previousYAxisValue = _yAxisValue;

        var remainingAxes = new List<PhysicalAxisState>();
        _hasXAxis = false;
        _hasYAxis = false;
        _xAxisValue = 0;
        _yAxisValue = 0;

        foreach (var axis in state.PhysicalAxes)
        {
            if (axis.Name.Equals("X", StringComparison.OrdinalIgnoreCase))
            {
                _hasXAxis = true;
                _xAxisValue = axis.Value;
                continue;
            }

            if (axis.Name.Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                _hasYAxis = true;
                _yAxisValue = axis.Value;
                continue;
            }

            remainingAxes.Add(axis);
        }

        while (Axes.Count < remainingAxes.Count)
        {
            Axes.Add(new AxisMonitorItem());
        }

        while (Axes.Count > remainingAxes.Count)
        {
            Axes.RemoveAt(Axes.Count - 1);
        }

        for (var index = 0; index < remainingAxes.Count; index++)
        {
            Axes[index].Name = remainingAxes[index].Name;
            Axes[index].Value = remainingAxes[index].Value;
        }

        while (Buttons.Count < state.PhysicalButtons.Count)
        {
            Buttons.Add(new ButtonMonitorItem());
        }

        while (Buttons.Count > state.PhysicalButtons.Count)
        {
            Buttons.RemoveAt(Buttons.Count - 1);
        }

        for (var index = 0; index < state.PhysicalButtons.Count; index++)
        {
            Buttons[index].Name = state.PhysicalButtons[index].Name;
            Buttons[index].IsPressed = state.PhysicalButtons[index].IsPressed;
        }

        while (Povs.Count < state.PhysicalPovs.Count)
        {
            Povs.Add(new PovMonitorItem());
        }

        while (Povs.Count > state.PhysicalPovs.Count)
        {
            Povs.RemoveAt(Povs.Count - 1);
        }

        for (var index = 0; index < state.PhysicalPovs.Count; index++)
        {
            Povs[index].Name = $"POV {state.PhysicalPovs[index].Index}";
            Povs[index].Direction = state.PhysicalPovs[index].DirectionDisplay;
            Povs[index].IsUp = state.PhysicalPovs[index].Up;
            Povs[index].IsRight = state.PhysicalPovs[index].Right;
            Povs[index].IsDown = state.PhysicalPovs[index].Down;
            Povs[index].IsLeft = state.PhysicalPovs[index].Left;
        }

        if (hadPositionAxes != HasPositionAxes)
        {
            OnPropertyChanged(nameof(HasPositionAxes));
            OnPropertyChanged(nameof(HasAnyAxes));
            OnPropertyChanged(nameof(HasPhysicalInputs));
        }

        if (hadAxes != HasAxes)
        {
            OnPropertyChanged(nameof(HasAxes));
            OnPropertyChanged(nameof(HasAnyAxes));
        }

        if (hadButtons != HasButtons)
        {
            OnPropertyChanged(nameof(HasButtons));
            OnPropertyChanged(nameof(HasPhysicalInputs));
        }

        if (hadPovs != HasPovs)
        {
            OnPropertyChanged(nameof(HasPovs));
            OnPropertyChanged(nameof(HasPhysicalInputs));
        }

        if (hadPhysicalInputs != HasPhysicalInputs)
        {
            OnPropertyChanged(nameof(HasPhysicalInputs));
        }

        if (!previousXAxisValue.Equals(_xAxisValue))
        {
            OnPropertyChanged(nameof(XAxisValue));
            OnPropertyChanged(nameof(PositionDisplay));
            OnPropertyChanged(nameof(PositionIndicatorLeft));
        }

        if (!previousYAxisValue.Equals(_yAxisValue))
        {
            OnPropertyChanged(nameof(YAxisValue));
            OnPropertyChanged(nameof(PositionDisplay));
            OnPropertyChanged(nameof(PositionIndicatorTop));
        }
    }
}

public sealed class AxisMonitorItem : ObservableObject
{
    private string _name = string.Empty;
    private double _value;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

public sealed class ButtonMonitorItem : ObservableObject
{
    private string _name = string.Empty;
    private bool _isPressed;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public bool IsPressed
    {
        get => _isPressed;
        set => SetProperty(ref _isPressed, value);
    }
}

public sealed class PovMonitorItem : ObservableObject
{
    private string _name = string.Empty;
    private string _direction = string.Empty;
    private bool _isUp;
    private bool _isRight;
    private bool _isDown;
    private bool _isLeft;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Direction
    {
        get => _direction;
        set => SetProperty(ref _direction, value);
    }

    public bool IsUp
    {
        get => _isUp;
        set
        {
            if (SetProperty(ref _isUp, value))
            {
                OnPropertyChanged(nameof(IsCentered));
            }
        }
    }

    public bool IsRight
    {
        get => _isRight;
        set
        {
            if (SetProperty(ref _isRight, value))
            {
                OnPropertyChanged(nameof(IsCentered));
            }
        }
    }

    public bool IsDown
    {
        get => _isDown;
        set
        {
            if (SetProperty(ref _isDown, value))
            {
                OnPropertyChanged(nameof(IsCentered));
            }
        }
    }

    public bool IsLeft
    {
        get => _isLeft;
        set
        {
            if (SetProperty(ref _isLeft, value))
            {
                OnPropertyChanged(nameof(IsCentered));
            }
        }
    }

    public bool IsCentered => !IsUp && !IsRight && !IsDown && !IsLeft;
}
