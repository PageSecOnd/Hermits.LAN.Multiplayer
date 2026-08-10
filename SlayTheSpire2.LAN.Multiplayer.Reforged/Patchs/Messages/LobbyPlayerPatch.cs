using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Unlocks;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Helpers;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Patchs.Messages
{
    [HarmonyPatch(typeof(StartRunLobbyPlayer), "Serialize")]
    internal class StartRunLobbyPlayerSerializePatch
    {
        private static bool Prefix(StartRunLobbyPlayer __instance, PacketWriter writer)
        {
            writer.WriteULong(__instance.id);
            PacketHelper.WriteVarInt(writer, (uint)__instance.slotId);
            writer.WriteModel(__instance.character);
            writer.Write(__instance.unlockState);
            writer.WriteInt(__instance.maxMultiplayerAscensionUnlocked);
            writer.Write(__instance.versionInfo);
            writer.WriteBool(__instance.isReady);

            return false;
        }
    }

    [HarmonyPatch(typeof(StartRunLobbyPlayer), "Deserialize")]
    internal class StartRunLobbyPlayerDeserializePatch
    {
        private static bool Prefix(ref StartRunLobbyPlayer __instance, PacketReader reader)
        {
            __instance.id = reader.ReadULong();
            __instance.slotId = (int)PacketHelper.ReadVarInt(reader);
            __instance.character = reader.ReadModel<CharacterModel>();
            __instance.unlockState = reader.Read<SerializableUnlockState>();
            __instance.maxMultiplayerAscensionUnlocked = reader.ReadInt();
            __instance.versionInfo = reader.Read<PeerVersionInfo>();
            __instance.isReady = reader.ReadBool();

            return false;
        }
    }
}
