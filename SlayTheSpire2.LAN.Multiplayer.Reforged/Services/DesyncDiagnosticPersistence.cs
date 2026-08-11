using System.Text;
using Godot;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Services
{
    internal static class DesyncDiagnosticPersistence
    {
        public static string? Save(DesyncDiagnosticReport report)
        {
            try
            {
                var directory = Path.Combine(ProjectSettings.GlobalizePath("user://"), "lan_desync_reports");
                Directory.CreateDirectory(directory);

                var fileName = $"desync_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{report.RemotePlayerId}.txt";
                var path = Path.Combine(directory, fileName);
                var text = new StringBuilder();

                text.AppendLine(report.ToLogText());
                text.AppendLine();
                text.AppendLine("Recent combat checkpoints (newest first):");

                var checkpoints = CombatStateSnapshotService.Instance.GetRecentCheckpoints();
                if (checkpoints.Count == 0)
                {
                    text.AppendLine("  <none>");
                }
                else
                {
                    foreach (var checkpoint in checkpoints)
                    {
                        text.AppendLine(
                            $"  #{checkpoint.Sequence} checksum={checkpoint.ChecksumId} " +
                            $"utc={checkpoint.CapturedAtUtc:O} context={checkpoint.Context} " +
                            $"flattenedValues={checkpoint.Snapshot.Count}");
                    }
                }

                File.WriteAllText(path, text.ToString(), Encoding.UTF8);
                GD.Print($"[LAN Multiplayer] Wrote desync diagnostic report: {path}");
                return path;
            }
            catch (Exception exception)
            {
                GD.PushWarning($"[LAN Multiplayer] Failed to persist desync diagnostic report: {exception}");
                return null;
            }
        }
    }
}
