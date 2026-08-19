using System;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;

namespace Wikiway.Plugin.Ui;

internal sealed class Fonts : IDisposable
{
    public IFontHandle Tag10 { get; }
    public IFontHandle Small11 { get; }
    public IFontHandle Citation12 { get; }
    public IFontHandle Body13 { get; }
    public IFontHandle Section { get; }
    public IFontHandle Body14 { get; }
    public IFontHandle Title17 { get; }
    public IFontHandle Brand18 { get; }

    public Fonts(IFontAtlas atlas)
    {
        Tag10 = Add(atlas, 10f);
        Small11 = Add(atlas, 11f);
        Citation12 = Add(atlas, 12f);
        Body13 = Add(atlas, 13f);
        Section = Add(atlas, 13.5f);
        Body14 = Add(atlas, 14f);
        Title17 = Add(atlas, 17f);
        Brand18 = Add(atlas, 18f);
    }

    // Requested sizes land on screen as-is - they do not follow the Dalamud
    // global scale on their own (confirmed at 150%) - so the delegate re-reads
    // the scale on every rebuild; the atlas rebuilds when the setting changes.
    // FontAwesome must be merged explicitly despite what the API docs claim -
    // without it the icon glyphs render as placeholders.
    private static IFontHandle Add(IFontAtlas atlas, float sizePx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var size = sizePx * ImGuiHelpers.GlobalScale;
            var config = new SafeFontConfig { SizePx = size };
            config.MergeFont = tk.AddDalamudDefaultFont(size, null);
            tk.AddFontAwesomeIconFont(config);
            tk.Font = config.MergeFont;
        }));

    public void Dispose()
    {
        Tag10.Dispose();
        Small11.Dispose();
        Citation12.Dispose();
        Body13.Dispose();
        Section.Dispose();
        Body14.Dispose();
        Title17.Dispose();
        Brand18.Dispose();
    }
}
