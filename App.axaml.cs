using Microsoft.Extensions.DependencyInjection;
using Avalonia.Controls.ApplicationLifetimes;
using RustyPainter.Service.Logging;
using RustyPainter.ViewModels;
using RustyPainter.Interface;
using RustyPainter.Library;
using RustyPainter.Painting;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia;
using System;

namespace RustyPainter
{
    public partial class App : Application
    {
        /// <summary>Loads the app's XAML resources.</summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>Resolves services from the DI container and creates the main window.</summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (Design.IsDesignMode)
            {
                base.OnFrameworkInitializationCompleted();
                return;
            }

            IServiceProvider services = Program.Services
                ?? throw new InvalidOperationException("ServiceProvider not initialized");

            IAppSettingsService settings = services.GetRequiredService<IAppSettingsService>();
            IUpdateService updates = services.GetRequiredService<IUpdateService>();
            IGameWindowService game = services.GetRequiredService<IGameWindowService>();
            IHotkeyService hotkeys = services.GetRequiredService<IHotkeyService>();
            IPaintEngineFactory engines = services.GetRequiredService<IPaintEngineFactory>();
            IHologramService hologram = services.GetRequiredService<IHologramService>();
            IStatusOverlayService overlay = services.GetRequiredService<IStatusOverlayService>();
            IProjectStore projects = services.GetRequiredService<IProjectStore>();
            IDialogService dialogs = services.GetRequiredService<IDialogService>();
            ISurfaceService surfaces = services.GetRequiredService<ISurfaceService>();

            settings.Initialize();

            // The logger starts before the container exists, so it prunes on a default window first
            // and only learns the user's retention preference once settings are loaded.
            AppLog.ApplyRetention(settings.Current.LogRetentionDays);
            AppLog.ApplyVerbose(settings.Current.VerboseLogging);

            MainWindowViewModel viewModel = new(updates, dialogs, settings, surfaces, game, hotkeys, engines, hologram, overlay, projects);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow(viewModel);

            base.OnFrameworkInitializationCompleted();
        }
    }
}
