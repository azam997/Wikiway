using System;
using Dalamud.Configuration;

namespace Wikiway.Plugin;

// Saved settings; Dalamud stores them as JSON in the plugin's config folder.
// Every feature toggle defaults on. ConfigWindow edits these and saves on each
// change.
[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool WikiSearchEnabled { get; set; } = true;
    public int MaxWikiResults { get; set; } = 5;
    public bool ContextMenuEnabled { get; set; } = true;
    public bool SoloDutyToastEnabled { get; set; } = true;
    public bool ShowUnlockRequirements { get; set; } = true;
    public bool ShowEquipmentStats { get; set; } = true;
    public bool ShowItemUsage { get; set; } = true;
    public bool ShowCutsceneAppearances { get; set; } = true;
    public bool CapNpcLocationPins { get; set; } = true;
    public bool ShowNearestAetheryte { get; set; } = true;
    public bool TeleportButtonEnabled { get; set; } = true;
    public bool ActiveQuestPickerEnabled { get; set; } = true;
    public bool SpoilerProtectionEnabled { get; set; } = true;
    public bool TutorialSeen { get; set; } = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
