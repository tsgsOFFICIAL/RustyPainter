using Microsoft.Extensions.DependencyInjection;
using RustyPainter.Service.Logging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;
using Avalonia;
using System;

namespace RustyPainter
{
    internal class Program
    {
        /// <summary>The app's DI container, built once at startup.</summary>
        public static IServiceProvider Services { get; private set; } = null!;

        /// <summary>Builds the DI container and starts the Avalonia application.</summary>
        [STAThread]
        public static void Main(string[] args)
        {
            AppLog.Initialize();
            AppLog.RegisterGlobalExceptionHandlers();
            AppLog.Info("Program", args.Length > 0
                ? $"Application starting ({string.Join(' ', args)})."
                : "Application starting.");

            try
            {
                Services = new ServiceCollection()
                    .AddSingleton<IAppSettingsService, AppSettingsService>()
                    .AddSingleton<IUpdateService, UpdateService>()
                    .AddSingleton<IDialogService, DialogService>()
                    .AddSingleton<ISurfaceService, SurfaceService>()
                    .AddSingleton<IGameWindowService, GameWindowService>()
                    .AddSingleton<IHotkeyService, GlobalHotkeyService>()
                    .AddSingleton<IPaintEngineFactory, PaintEngineFactory>()
                    .AddSingleton<IHologramService, HologramService>()
                    .BuildServiceProvider();

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                AppLog.Fatal("Program", "Unhandled exception during startup.", ex);
                throw;
            }
            finally
            {
                AppLog.Info("Program", "Application exiting.");
            }
        }

        /// <summary>Configures the Avalonia <see cref="AppBuilder"/> used to start the application.</summary>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
