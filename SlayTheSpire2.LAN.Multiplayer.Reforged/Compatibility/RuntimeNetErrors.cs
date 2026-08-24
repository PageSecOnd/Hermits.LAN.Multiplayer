using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Compatibility;

/// <summary>
/// Beta reserves numeric ranges in NetError while stable uses contiguous values.
/// Looking values up by name prevents a stable-built DLL from sending the wrong
/// reason code on beta.
/// </summary>
internal static class RuntimeNetErrors
{
    public static readonly NetError CancelledJoin = Parse(nameof(CancelledJoin));
    public static readonly NetError InternalError = Parse(nameof(InternalError));
    public static readonly NetError Kicked = Parse(nameof(Kicked));
    public static readonly NetError Timeout = Parse(nameof(Timeout));
    public static readonly NetError UnknownNetworkError = Parse(nameof(UnknownNetworkError));

    private static NetError Parse(string name)
    {
        if (Enum.TryParse(name, ignoreCase: false, out NetError value))
            return value;

        throw new NotSupportedException($"The game no longer defines NetError.{name}.");
    }
}
