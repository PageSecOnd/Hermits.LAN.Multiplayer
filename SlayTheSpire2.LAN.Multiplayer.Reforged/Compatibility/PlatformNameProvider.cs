using System.Reflection;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Compatibility;

internal static class PlatformNameProvider
{
    public static string GetDefaultPlayerName()
    {
        try
        {
            var steamFriends = Type.GetType("Steamworks.SteamFriends, Steamworks.NET", throwOnError: false);
            var getPersonaName = steamFriends?.GetMethod("GetPersonaName",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (getPersonaName?.Invoke(null, null) is string { Length: > 0 } steamName)
                return steamName;
        }
        catch
        {
            // Steam is optional for direct LAN play.
        }

        return string.IsNullOrWhiteSpace(Environment.UserName) ? "LAN Player" : Environment.UserName;
    }
}
