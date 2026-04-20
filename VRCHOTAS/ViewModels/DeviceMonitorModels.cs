using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VRCHOTAS.Models;

namespace VRCHOTAS.ViewModels;

public sealed class DeviceMonitorGroup : ObservableObject
{
    private string _deviceId = string.Empty;
    private string _deviceName = string.Empty;
    private bool _isConnected;

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

    public void UpdateFrom(JoystickDeviceState state)
    {
        var axisKeys = state.Axes.Keys.ToList();
        while (Axes.Count < axisKeys.Count)
        {
            Axes.Add(new AxisMonitorItem());
        }

        while (Axes.Count > axisKeys.Count)
        {
            Axes.RemoveAt(Axes.Count - 1);
        }

        for (var index = 0; index < axisKeys.Count; index++)
        {
            var key = axisKeys[index];
            Axes[index].Name = key;
            Axes[index].Value = state.Axes[key];
        }

        while (Buttons.Count < state.Buttons.Count)
        {
            Buttons.Add(new ButtonMonitorItem());
        }

        while (Buttons.Count > state.Buttons.Count)
        {
            Buttons.RemoveAt(Buttons.Count - 1);
        }

        for (var index = 0; index < state.Buttons.Count; index++)
        {
            Buttons[index].Name = index.ToString();
            Buttons[index].IsPressed = state.Buttons[index];
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
