using System;
using System.Diagnostics;

namespace Wikiway.Plugin.GameIntegration;

internal static class BrowserOpener
{
    public static void Open(Uri url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Could not open browser for {Url}", url);
        }
    }
}
