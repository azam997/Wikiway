using System.IO;
using System.Net.Http;
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

    public Configuration Configuration { get; }
    public IQueryPipeline Pipeline { get; }
    public ICacheStore CacheStore { get; }
    internal Fonts Fonts { get; }
    public readonly WindowSystem WindowSystem = new("Wikiway");

    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly TutorialWindow tutorialWindow;
    private readonly HttpClient httpClient;
    private readonly ContextMenuIntegration contextMenuIntegration;
    private readonly SoloDutyNotifier soloDutyNotifier;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Fonts = new Fonts(PluginInterface.UiBuilder.FontAtlas);

        CacheStore = new FileCacheStore(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "cache"));
        httpClient = new HttpClient(new CachingHandler(CacheStore)
        {
            InnerHandler = new ThrottlingHandler { InnerHandler = new HttpClientHandler() },
        });
        var wikiClient = new ConsoleGamesWikiClient(httpClient);
        var gameDataStore = new LuminaGameDataStore(DataManager.GameData);

        Pipeline = new QueryOrchestrator(
            [
                new LocalGameDataProvider(gameDataStore),
                new ConsoleGamesWikiProvider(
                    wikiClient,
                    () => Configuration.WikiSearchEnabled,
                    () => Configuration.MaxWikiResults),
            ],
            new QueryNormalizer(),
            new ResultRanker());

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
                "or scope it: /wikiway quest:the ultimate weapon (item:, quest:, duty:, npc:)",
        });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /wikiway.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);

        WindowSystem.RemoveAllWindows();
        soloDutyNotifier.Dispose();
        contextMenuIntegration.Dispose();
        mainWindow.Dispose();
        httpClient.Dispose();
        Fonts.Dispose();
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
                "quest" => SearchCategory.Quests,
                "duty" => SearchCategory.Duties,
                "npc" => SearchCategory.Npcs,
                _ => (SearchCategory?)null,
            };
            if (category is { } picked)
                return (query[(colon + 1)..].Trim(), picked);
        }

        return (query, SearchCategory.Other);
    }
}
