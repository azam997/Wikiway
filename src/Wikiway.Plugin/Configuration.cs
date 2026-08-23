using System;
using Dalamud.Configuration;

namespace Wikiway.Plugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool WikiSearchEnabled { get; set; } = true;
    public int MaxWikiResults { get; set; } = 5;
    public bool ContextMenuEnabled { get; set; } = true;
    public bool SoloDutyToastEnabled { get; set; } = true;
    public bool ShowUnlockRequirements { get; set; } = true;
    public bool ShowCutsceneAppearances { get; set; } = true;
    public bool CapNpcLocationPins { get; set; } = true;
    public bool ActiveQuestPickerEnabled { get; set; } = true;
    public bool SpoilerProtectionEnabled { get; set; } = true;
    public bool TutorialSeen { get; set; } = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
