namespace GameCubeAppStore.Console;

/// <summary>
/// Replace with the HTTP-only address of the public catalog gateway reachable
/// from your GameCube. Do NOT expose developer/account endpoints over HTTP.
/// </summary>
internal static class StoreSettings
{
    public const string PublicCatalogOrigin = "http://192.168.1.100:5080";
}
