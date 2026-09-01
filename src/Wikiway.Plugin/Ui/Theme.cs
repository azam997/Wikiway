using System.Numerics;

namespace Wikiway.Plugin.Ui;

// Nocturne token block; hex values are authoritative in the design handoffs.
// Accent ramp is the original purple; the Highlight steps keep the Flare pink
// for inline emphasis and the wiki tag.
internal static class Theme
{
    public static readonly Vector4 Bg = FromHex(0x161826);
    public static readonly Vector4 Surface = FromHex(0x232532);
    public static readonly Vector4 Text = FromHex(0xe9e9ed);
    public static readonly Vector4 Accent = FromHex(0x9184d9);
    public static readonly Vector4 Accent100 = FromHex(0xf5f4ff);
    public static readonly Vector4 Accent300 = FromHex(0xd2cefd);
    public static readonly Vector4 Accent400 = FromHex(0xb5abfc);
    public static readonly Vector4 Accent700 = FromHex(0x5d5294);
    public static readonly Vector4 Accent800 = FromHex(0x423a6a);
    public static readonly Vector4 Accent900 = FromHex(0x2b2741);
    public static readonly Vector4 Highlight = FromHex(0xff6bc4);
    public static readonly Vector4 Highlight100 = FromHex(0xfff1f8);
    public static readonly Vector4 Highlight800 = FromHex(0x6f1b51);
    public static readonly Vector4 Neutral100 = FromHex(0xf3f5fe);
    public static readonly Vector4 Neutral300 = FromHex(0xcfd3e5);
    public static readonly Vector4 Neutral400 = FromHex(0xb2b6ca);
    public static readonly Vector4 Neutral500 = FromHex(0x9397ab);
    public static readonly Vector4 Neutral600 = FromHex(0x75798c);
    public static readonly Vector4 Neutral700 = FromHex(0x595d6c);
    public static readonly Vector4 Neutral800 = FromHex(0x3f424d);
    public static readonly Vector4 Divider = FromHex(0xe9e9ed, 0.16f);
    public static readonly Vector4 AccentHover = FromHex(0x9184d9, 0.10f);
    public static readonly Vector4 AccentPressed = FromHex(0x9184d9, 0.18f);
    public static readonly Vector4 HighlightHover = FromHex(0xff6bc4, 0.10f);
    public static readonly Vector4 HighlightPressed = FromHex(0xff6bc4, 0.18f);

    public static readonly uint AccentU = Pack(Accent);
    public static readonly uint Accent100U = Pack(Accent100);
    public static readonly uint Accent300U = Pack(Accent300);
    public static readonly uint Accent700U = Pack(Accent700);
    public static readonly uint Accent800U = Pack(Accent800);
    public static readonly uint Accent900U = Pack(Accent900);
    public static readonly uint Highlight100U = Pack(Highlight100);
    public static readonly uint Highlight800U = Pack(Highlight800);
    public static readonly uint Neutral100U = Pack(Neutral100);
    public static readonly uint Neutral400U = Pack(Neutral400);
    public static readonly uint Neutral500U = Pack(Neutral500);
    public static readonly uint Neutral600U = Pack(Neutral600);
    public static readonly uint Neutral800U = Pack(Neutral800);
    public static readonly uint SurfaceU = Pack(Surface);
    public static readonly uint DividerU = Pack(Divider);
    public static readonly uint RowDividerU = Pack(FromHex(0xe9e9ed, 0.08f));

    public const float Space1 = 2.8f;
    public const float Space2 = 5.6f;
    public const float Space3 = 8.4f;
    public const float Space4 = 11.2f;
    public const float Space6 = 16.8f;
    public const float Space8 = 22.4f;

    public const float RadiusSm = 4f;
    public const float RadiusMd = 8f;
    public const float RadiusLg = 14f;

    private static Vector4 FromHex(uint rgb, float alpha = 1f) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        alpha);

    private static uint Pack(Vector4 c) =>
        ((uint)((c.W * 255) + 0.5f) << 24) | ((uint)((c.Z * 255) + 0.5f) << 16) |
        ((uint)((c.Y * 255) + 0.5f) << 8) | (uint)((c.X * 255) + 0.5f);
}
