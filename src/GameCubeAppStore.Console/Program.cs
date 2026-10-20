using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Network;

namespace GameCubeAppStore.Console;

/// <summary>
/// First-stage connectivity harness. The storefront screen awaits visual approval.
/// </summary>
internal static class Program
{
    private static async Task<int> Main()
    {
        DebugOverlay.WriteLine("GAMECUBE APP STORE");
        DebugOverlay.WriteLine("Starting network...");
        try
        {
            await NetworkRuntime.StartAsync(new NetworkOptions(), CancellationToken.None);
            using var catalog = new CatalogClient(StoreSettings.PublicCatalogOrigin);
            var items = await catalog.ListAsync(CancellationToken.None);
            DebugOverlay.WriteLine("CATALOG ENTRIES: " + items.Count);
            for (int i = 0; i < items.Count && i < 8; i++)
                DebugOverlay.WriteLine(items[i].Title + "  " + items[i].Version);
            return 0;
        }
        catch (Exception ex)
        {
            DebugOverlay.WriteLine("STORE CONNECTION FAILED");
            DebugOverlay.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            if (NetworkRuntime.IsReady) NetworkRuntime.Stop();
        }
    }
}
