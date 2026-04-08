using System.Windows;
using System.Windows.Threading;
using VRCHOTAS.Logging;
using VRCHOTAS.Models;
using VRCHOTAS.ViewModels;

namespace VRCHOTAS;

public partial class MappingEditorWindow : Window
{
    private readonly IAppLogger _logger;
    private readonly MappingEditorViewModel _viewModel;
    private readonly DispatcherTimer _detectTimer;

    public MappingEditorWindow(IAppLogger logger, Func<RawJoystickState> stateProvider, MappingEntry? existing)
    {
        InitializeComponent();
        _logger = logger;
        _viewModel = new MappingEditorViewModel(stateProvider, existing);
        DataContext = _viewModel;
        _viewModel.IsListening = true;
        _logger.Info(nameof(MappingEditorWindow), "Source detection started automatically.");

        _detectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _detectTimer.Tick += (_, _) => DetectTick();
        _detectTimer.Start();
        Closed += (_, _) => _detectTimer.Stop();
    }

    public MappingEntry? MappingResult { get; private set; }

    private void DetectTick()
    {
        _viewModel.UpdateLivePreview();

        if (!_viewModel.IsListening)
        {
            return;
        }

        if (_viewModel.TryAutoDetectSource())
        {
            _viewModel.IsListening = false;
            _logger.Info(nameof(MappingEditorWindow), "Source detection completed.");
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            MappingResult = _viewModel.BuildResult();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _logger.Warning(nameof(MappingEditorWindow), "Mapping save validation failed.");
            MessageBox.Show(this, ex.Message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
