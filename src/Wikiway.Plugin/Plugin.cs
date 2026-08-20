using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Caching;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Wikiway.Core.Providers;
using Wikiway.Core.Wiki;
using Wikiway.GameData;
using Wikiway.Plugin.GameIntegration;
using Wikiway.Plugin.Ui;
using Wikiway.Plugin.Windows;

namespace Wikiway.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    private const string CommandName = "/wikiway";
    private const string CommandAlias = "/wway";
    private const string CommandShort = "/ww";

    public Configuration Configuration { get; }
    public IQueryPipeline Pipeline { get; }
    public ICacheStore CacheStore { get; }
    internal Fonts Fonts { get; }
    internal QuestProgressTracker QuestProgress { get; }
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
        var storedConfig = PluginInterface.GetPluginConfig();
        Configuration = storedConfig as Configuration ?? new Configuration();
        if (storedConfig != null && !ReferenceEquals(Configuration, storedConfig))
            Log.Warning("Stored configuration was unreadable; starting with defaults.");
        Fonts = new Fonts(PluginInterface.UiBuilder.FontAtlas);

        var fileCache = new FileCacheStore(
            Path.Combine(PluginInterface.GetPluginConfigDirectory(), "cache"),
            message => Log.Warning("{Message}", message));
        CacheStore = fileCache;
        httpClient = new HttpClient(new CachingHandler(CacheStore)
        {
            InnerHandler = new ThrottlingHandler { InnerHandler = new HttpClientHandler() },
        });
        var wikiClient = new ConsoleGamesWikiClient(httpClient);
        var gameDataStore = new LuminaGameDataStore(DataManager.GameData);
        QuestProgress = new QuestProgressTracker();

        localProvider = new LocalGameDataProvider(
            gameDataStore,
            id => !Configuration.SpoilerProtectionEnabled
                || !QuestProgress.IsAvailable
                || QuestProgress.IsComplete(id),
            shutdownCts.Token);
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

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Look something up. /wikiway <question or name>, " +
                "or scope it: /wikiway quest:the ultimate weapon (item:, quest:, gather:, npc:, unlock:)",
        });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /wikiway.",
        });
        CommandManager.AddHandler(CommandShort, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /wikiway.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    }

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
        // this assembly; give them a moment to observe cancellation before
        // both go away with the ALC.
        Task[] background = [warmupTask, localProvider.IndexTask, .. mainWindow.ActiveSearches()];
        try
        {
            Task.WhenAll(background).Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Cancelled or faulted is fine - quiescence is all that matters.
        }

        httpClient.Dispose();
        Fonts.Dispose();
        shutdownCts.Dispose();
    }

    public void ToggleMainUi() => mainWindow.Toggle();
    public void ToggleConfigUi() => configWindow.Toggle();
    public void ShowTutorial() => tutorialWindow.Restart();

    private void OnCommand(string command, string args)
    {
        var query = args.Trim();
        if (query.Length > 0)
        {
            var (term, category) = ParseCategoryPrefix(query);
            mainWindow.SubmitQuery(term, category);
        }

        mainWindow.IsOpen = true;
    }

    private static (string Term, SearchCategory Category) ParseCategoryPrefix(string query)
    {
        var colon = query.IndexOf(':');
        if (colon > 0)
        {
            var category = query[..colon].ToLowerInvariant() switch
            {
                "item" => SearchCategory.Items,
                "gather" or "gathering" => SearchCategory.Gathering,
                "npc" => SearchCategory.Npcs,
                // "area" survives as an alias from when the tab was named Areas;
                // "duty" from when duties had their own tab - Other covers them now.
                // "quest" and "unlock" share the merged Quests & Unlocks tab.
                "quest" or "unlock" or "unlockable" or "area" => SearchCategory.Unlocks,
                "duty" => SearchCategory.Other,
                _ => (SearchCategory?)null,
            };
            if (category is { } picked)
                return (query[(colon + 1)..].Trim(), picked);
        }

        return (query, SearchCategory.Other);
    }
}
