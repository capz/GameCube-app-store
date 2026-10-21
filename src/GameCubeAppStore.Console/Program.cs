using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Network;

namespace GameCubeAppStore.Console;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            DebugOverlay.WriteLine("GAMECUBE APP STORE - NETWORK STARTUP");
            await NetworkRuntime.StartAsync(new NetworkOptions(),CancellationToken.None);
            var client=new CatalogClient(StoreSettings.PublicCatalogOrigin);
            using var draw=new Draw2D(GraphicsDevice.Default,
                new Draw2DOptions{Width=960,Height=720});
            using var text=new SampleText();
            using var screen=new StoreScreen(client,draw,text);
            return screen.Run();
        }
        catch(Exception error)
        {
            DebugOverlay.WriteLine("STORE FAILED: "+error.Message);
            return 1;
        }
        finally
        {
            if(NetworkRuntime.IsReady)NetworkRuntime.Stop();
        }
    }
}
