using RustyPainter.ViewModels;
using RustyPainter.Service;
using Avalonia.Controls;
using System;

namespace RustyPainter
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;

        /// <summary>Creates the window with a design-time view model, for the Avalonia previewer.</summary>
        public MainWindow() : this(CreateDesignViewModel()) { }

        /// <summary>
        /// Creates a fully-wired view model for the Avalonia previewer. The update check never actually
        /// runs there, since it is gated behind <see cref="Design.IsDesignMode"/> below.
        /// </summary>
        private static MainWindowViewModel CreateDesignViewModel()
        {
            AppSettingsService settings = new();
            settings.Initialize();

            GameWindowService game = new();
            return new MainWindowViewModel(new UpdateService(), new DialogService(), settings, new SurfaceService(), game, new GlobalHotkeyService(game), new PaintEngineFactory(game));
        }

        /// <summary>Creates the window bound to the given view model and wires up the startup update check.</summary>
        public MainWindow(MainWindowViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();

            if (!Design.IsDesignMode)
                Opened += OnMainWindowOpened;
        }

        /// <summary>Kicks off the startup update check once the window is shown. Runs once per launch.</summary>
        private async void OnMainWindowOpened(object? sender, EventArgs e)
        {
            Opened -= OnMainWindowOpened;
            await _viewModel.CheckForUpdatesOnStartupAsync();
        }
    }
}
