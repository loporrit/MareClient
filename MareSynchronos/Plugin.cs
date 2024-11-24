using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.PlayerData.Services;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Components.Popup;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MareSynchronos;

namespace LoporritSync;

public sealed class Plugin : IDalamudPlugin
{
    private readonly CancellationTokenSource _pluginCts = new();
    private readonly Task _hostBuilderRunTask;

    public static Plugin Self;
    public Action<IFramework> _realOnFrameworkUpdate = null;

    // Proxy function in the LoporritSync namespace to avoid confusion in /xlstats
    public void OnFrameworkUpdate(IFramework framework)
    {
        if (_realOnFrameworkUpdate != null)
            _realOnFrameworkUpdate(framework);
    }

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IDataManager gameData,
        IFramework framework, IObjectTable objectTable, IClientState clientState, ICondition condition, IChatGui chatGui,
        IGameGui gameGui, IDtrBar dtrBar, IToastGui toastGui, IPluginLog pluginLog, ITargetManager targetManager, IGameLifecycle addonLifecycle,
        INotificationManager notificationManager, ITextureProvider textureProvider, IContextMenu contextMenu, IGameInteropProvider gameInteropProvider)
    {
        Plugin.Self = this;
        _hostBuilderRunTask = new HostBuilder()
        .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddDalamudLogging(pluginLog);
            lb.SetMinimumLevel(LogLevel.Trace);
        })
        .ConfigureServices(collection =>
        {
            collection.AddSingleton(new WindowSystem("LoporritSync"));
            collection.AddSingleton<FileDialogManager>();
            collection.AddSingleton(new Dalamud.Localization("MareSynchronos.Localization.", "", useEmbedded: true));

            // add mare related singletons
            collection.AddSingleton<MareMediator>();
            collection.AddSingleton<FileCacheManager>();
            collection.AddSingleton<ServerConfigurationManager>();
            collection.AddSingleton<PairManager>((s) => new PairManager(s.GetRequiredService<ILogger<PairManager>>(), s.GetRequiredService<PairFactory>(),
                s.GetRequiredService<MareConfigService>(), s.GetRequiredService<MareMediator>(), contextMenu));
            collection.AddSingleton<ApiController>();
            collection.AddSingleton<MareCharaFileManager>();
            collection.AddSingleton<PerformanceCollectorService>();
            collection.AddSingleton<HubFactory>();
            collection.AddSingleton<FileUploadManager>();
            collection.AddSingleton<FileTransferOrchestrator>();
            collection.AddSingleton<MarePlugin>();
            collection.AddSingleton<MareProfileManager>();
            collection.AddSingleton<GameObjectHandlerFactory>();
            collection.AddSingleton<FileDownloadManagerFactory>();
            collection.AddSingleton((s) => new PairHandlerFactory(s.GetRequiredService<ILoggerFactory>(), s.GetRequiredService<GameObjectHandlerFactory>(),
                s.GetRequiredService<IpcManager>(), s.GetRequiredService<FileDownloadManagerFactory>(), s.GetRequiredService<DalamudUtilService>(),
                s.GetRequiredService<PluginWarningNotificationService>(), s.GetRequiredService<ServerConfigurationManager>(),
                CancellationTokenSource.CreateLinkedTokenSource(addonLifecycle.GameShuttingDownToken, addonLifecycle.DalamudUnloadingToken).Token,
                s.GetRequiredService<FileCacheManager>(), s.GetRequiredService<MareMediator>()));
            collection.AddSingleton<PairFactory>();
            collection.AddSingleton<CharacterAnalyzer>();
            collection.AddSingleton<TokenProvider>();
            collection.AddSingleton<PluginWarningNotificationService>();
            collection.AddSingleton<FileCompactor>();
            collection.AddSingleton<TagHandler>();
            collection.AddSingleton<UidDisplayHandler>();
            collection.AddSingleton((s) => new DalamudUtilService(s.GetRequiredService<ILogger<DalamudUtilService>>(),
                clientState, objectTable, framework, gameGui, toastGui, condition, gameData, targetManager,
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<PerformanceCollectorService>()));
            collection.AddSingleton((s) => new DtrEntry(s.GetRequiredService<ILogger<DtrEntry>>(), dtrBar, s.GetRequiredService<MareConfigService>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<PairManager>(), s.GetRequiredService<ApiController>()));
            collection.AddSingleton((s) => new IpcManager(s.GetRequiredService<ILogger<IpcManager>>(),
                pluginInterface, s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MareMediator>()));

            collection.AddSingleton((s) => new MareConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new SyncshellConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ConfigurationMigrator(s.GetRequiredService<ILogger<ConfigurationMigrator>>(), pluginInterface, s.GetRequiredService<NotesConfigService>()));
            collection.AddSingleton<HubFactory>();

            // add scoped services
            collection.AddScoped<PeriodicFileScanner>();
            collection.AddScoped<UiFactory>();
            collection.AddScoped<WindowMediatorSubscriberBase, SettingsUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, CompactUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, GposeUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, IntroUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DownloadUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, PopoutProfileUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DataAnalysisUi>();

            collection.AddScoped<WindowMediatorSubscriberBase, EditProfileUi>((s) => new EditProfileUi(s.GetRequiredService<ILogger<EditProfileUi>>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<ApiController>(), pluginInterface.UiBuilder, s.GetRequiredService<UiSharedService>(),
                s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<MareProfileManager>()));
            collection.AddScoped<WindowMediatorSubscriberBase, PopupHandler>();
            collection.AddScoped<IPopupHandler, ReportPopupHandler>();
            collection.AddScoped<IPopupHandler, BanUserPopupHandler>();
            collection.AddScoped<CacheCreationService>();
            collection.AddScoped<TransientResourceManager>();
            collection.AddScoped<PlayerDataFactory>();
            collection.AddScoped<OnlinePlayerManager>();
            collection.AddScoped((s) => new UiService(s.GetRequiredService<ILogger<UiService>>(), pluginInterface.UiBuilder, s.GetRequiredService<MareConfigService>(),
                s.GetRequiredService<WindowSystem>(), s.GetServices<WindowMediatorSubscriberBase>(),
                s.GetRequiredService<UiFactory>(),
                s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareMediator>()));
            collection.AddScoped((s) => new CommandManagerService(commandManager, s.GetRequiredService<PerformanceCollectorService>(),
                s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<PeriodicFileScanner>(), s.GetRequiredService<ChatService>(),
                s.GetRequiredService<ApiController>(), s.GetRequiredService<MareMediator>(), s.GetRequiredService<MareConfigService>()));
            collection.AddScoped((s) => new NotificationService(s.GetRequiredService<ILogger<NotificationService>>(),
                s.GetRequiredService<MareMediator>(), notificationManager, chatGui, s.GetRequiredService<MareConfigService>()));
            collection.AddScoped((s) => new UiSharedService(s.GetRequiredService<ILogger<UiSharedService>>(), s.GetRequiredService<IpcManager>(), s.GetRequiredService<ApiController>(),
                s.GetRequiredService<PeriodicFileScanner>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MareConfigService>(), s.GetRequiredService<DalamudUtilService>(),
                pluginInterface, textureProvider, s.GetRequiredService<Dalamud.Localization>(), s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<MareMediator>()));
            collection.AddScoped((s) => new ChatService(s.GetRequiredService<ILogger<ChatService>>(), s.GetRequiredService<DalamudUtilService>(),
                s.GetRequiredService<MareMediator>(), s.GetRequiredService<ApiController>(), s.GetRequiredService<PairManager>(),
                s.GetRequiredService<ILogger<GameChatHooks>>(), gameInteropProvider, chatGui,
                s.GetRequiredService<MareConfigService>(), s.GetRequiredService<ServerConfigurationManager>()));

            collection.AddHostedService(p => p.GetRequiredService<MareMediator>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationMigrator>());
            collection.AddHostedService(p => p.GetRequiredService<DalamudUtilService>());
            collection.AddHostedService(p => p.GetRequiredService<PerformanceCollectorService>());
            collection.AddHostedService(p => p.GetRequiredService<DtrEntry>());
            collection.AddHostedService(p => p.GetRequiredService<MarePlugin>());
        })
        .Build()
        .RunAsync(_pluginCts.Token);
    }

    public void Dispose()
    {
        _pluginCts.Cancel();
        _pluginCts.Dispose();
        _hostBuilderRunTask.Wait();
    }
}
