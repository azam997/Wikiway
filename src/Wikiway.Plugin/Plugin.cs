using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Configuration;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Caching;
using Wikiway.Core.Pipeline;
using Wikiway.Core.Providers;
using Wikiway.Core.Wiki;
using Wikiway.GameData;
using Wikiway.Plugin.GameIntegration;
using Wikiway.Plugin.Ui;
using Wikiway.Plugin.Windows;

namespace Wikiway.Plugin;

// Entry point Dalamud loads. The constructor builds every service once: config,
// fonts, the wiki HTTP stack, the game-data store, the search pipeline, the
// windows and the game hooks. Dispose takes them down in reverse. This is the
// only project that references Dalamud; Core and GameData stay plain .NET.
public sealed class Plugin : IDalamudPlugin
{
    // Dalamud services, injected into these statics before the constructor
    // runs; the rest of the plugin reaches them as Plugin.X.
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IAetheryteList AetheryteList { get; private set; } = null!;

    private const string CommandName = "/wikiway";
    private const string CommandAlias = "/wway";
    private const string CommandShort = "/ww";

    public Configuration Configuration { get; }
    public IQueryPipeline Pipeline { get; }
    public ICacheStore CacheStore { get; }
    internal Fonts Fonts { get; }
    internal QuestProgressTracker QuestProgress { get; }
    internal TeleportService Teleport { get; }
    public readonly WindowSystem WindowSystem = new("Wikiway");

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly TutorialWindow tutorialWindow;
    private readonly HttpClient httpClient;
    private readonly ContextMenuIntegration contextMenuIntegration;
    private readonly SoloDutyNotifier soloDutyNotifier;
    private readonly CancellationTokenSource shutdownCts = new();
    private readonly LocalGameDataProvider localProvider;
    private readonly Task warmupTask;

    public Plugin()
    {
        try
        {
            var storedConfig = LoadConfiguration();
            Configuration = storedConfig as Configuration ?? new Configuration();
            if (storedConfig != null && !ReferenceEquals(Configuration, storedConfig))
                Log.Warning("Stored configuration was unreadable; starting with defaults.");
            Fonts = new Fonts(PluginInterface.UiBuilder.FontAtlas);

            var fileCache = new FileCacheStore(
                Path.Combine(PluginInterface.GetPluginConfigDirectory(), "cache"),
                message => Log.Warning("{Message}", message));
            CacheStore = fileCache;
            // One HttpClient for all wiki traffic. Handlers run outside-in: the
            // disk cache (a hit never reaches the network), then the throttle
            // (one request per second, retries on 429/502/503), then the socket
            // handler.
            httpClient = new HttpClient(new CachingHandler(CacheStore)
            {
                InnerHandler = new ThrottlingHandler
                {
                    InnerHandler = new HttpClientHandler
                    {
                        AutomaticDecompression = System.Net.DecompressionMethods.All,
                    },
                },
            });
            var wikiClient = new ConsoleGamesWikiClient(httpClient);
            var gameDataStore = new LuminaGameDataStore(DataManager.GameData);
            QuestProgress = new QuestProgressTracker();
            Teleport = new TeleportService();

            // Spoiler hiding is off (fails open) while logged out or before the
            // first progress snapshot: a lookup tool that hides data it can't
            // verify would be wrong more often than it protects.
            localProvider = new LocalGameDataProvider(
                gameDataStore,
                id => !Configuration.SpoilerProtectionEnabled
                    || !QuestProgress.IsAvailable
                    || QuestProgress.IsComplete(id),
                shutdownCts.Token);
            // The search pipeline: game data and the wiki, both queried for
            // every search (the wiki only while enabled in settings).
            Pipeline = new QueryOrchestrator(
                [
                    localProvider,
                    new ConsoleGamesWikiProvider(
                        wikiClient,
                        () => Configuration.WikiSearchEnabled,
                        () => Configuration.MaxWikiResults),
                ],
                new QueryNormalizer(),
                new ResultRanker());

            // Game-thread callbacks and first searches otherwise pay for the lazy
            // lookup builds; warm everything on one background task instead.
            warmupTask = Task.Run(() =>
            {
                try
                {
                    gameDataStore.WarmAll(shutdownCts.Token);
                    fileCache.Sweep(TimeSpan.FromDays(7), shutdownCts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    Log.Warning(e, "Background lookup warm-up failed.");
                }
            });

            mainWindow = new MainWindow(this);
            configWindow = new ConfigWindow(this);
            tutorialWindow = new TutorialWindow(Configuration);
            WindowSystem.AddWindow(mainWindow);
            WindowSystem.AddWindow(configWindow);
            WindowSystem.AddWindow(tutorialWindow);

            if (!Configuration.TutorialSeen)
                tutorialWindow.IsOpen = true;

            contextMenuIntegration = new ContextMenuIntegration(mainWindow, gameDataStore, Configuration);
            soloDutyNotifier = new SoloDutyNotifier(mainWindow, gameDataStore, Configuration);

            AddCommand(CommandName,
                "Look something up. /wikiway <question or name>, " +
                "or scope it: /wikiway quest:the ultimate weapon (item:, quest:, gather:, npc:, unlock:)");
            AddCommand(CommandAlias, "Alias for /wikiway.");
            AddCommand(CommandShort, "Alias for /wikiway.");

            PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        }
        catch
        {
            // Dalamud never calls Dispose on a constructor that threw. Its
            // scoped services take back the commands, events and font atlas,
            // but the index build and warm-up would keep walking sheets and
            // keep this assembly loaded until the game exits.
            shutdownCts.Cancel();
            httpClient?.Dispose();
            Fonts?.Dispose();
            throw;
        }
    }

    // Stop new work first (cancel, unhook UI, commands and game hooks, close
    // windows), give background tasks up to two seconds to notice, then free
    // the HTTP stack and fonts.
    public void Dispose()
    {
        shutdownCts.Cancel();

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);
        CommandManager.RemoveHandler(CommandShort);

        WindowSystem.RemoveAllWindows();
        soloDutyNotifier.Dispose();
        contextMenuIntegration.Dispose();
        mainWindow.Dispose();

        // In-flight searches and the index build still hold the HttpClient and
        // this assembly; give them a moment to notice the cancellation before
        // both are unloaded with the plugin's AssemblyLoadContext.
        Task[] background = mainWindow.PendingSearch is { } search
            ? [warmupTask, localProvider.IndexTask, search]
            : [warmupTask, localProvider.IndexTask];
        try
        {
            Task.WhenAll(background).Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Cancelled or faulted is fine; they only need to have stopped.
        }

        httpClient.Dispose();
        Fonts.Dispose();
        shutdownCts.Dispose();
    }

    // Dalamud deserializes the config file with no error handling of its own,
    // so a truncated Wikiway.json (a crash mid-save) would otherwise fail the
    // constructor and leave the plugin unloadable until the file is deleted.
    private static IPluginConfiguration? LoadConfiguration()
    {
        try
        {
            return PluginInterface.GetPluginConfig();
        }
        catch (Exception e)
        {
            Log.Error(e, "Stored configuration could not be read; starting with defaults.");
            BackUpConfigFile();
            return null;
        }
    }

    private static void BackUpConfigFile()
    {
        try
        {
            var file = PluginInterface.ConfigFile;
            if (file.Exists)
                file.CopyTo(file.FullName + ".corrupt", overwrite: true);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not back up the unreadable configuration file.");
        }
    }

    private void AddCommand(string name, string help)
    {
        // AddHandler fails when another plugin already owns the command -
        // likeliest for the short /ww alias.
        if (!CommandManager.AddHandler(name, new CommandInfo(OnCommand) { HelpMessage = help }))
            Log.Warning("Command {Command} is already registered by another plugin.", name);
    }

    public void ToggleMainUi() => mainWindow.Toggle();
    public void ToggleConfigUi() => configWindow.Toggle();
    public void ShowTutorial() => tutorialWindow.Restart();

    // /wikiway <text> runs that search; /wikiway alone just opens the window.
    private void OnCommand(string command, string args)
    {
        var query = args.Trim();
        if (query.Length > 0)
            mainWindow.SubmitQuery(query);

        mainWindow.IsOpen = true;
    }
}
