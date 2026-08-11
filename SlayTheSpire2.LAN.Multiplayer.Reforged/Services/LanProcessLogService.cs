using System.Text;
using Godot;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Services
{
    /// <summary>
    /// Writes LAN-specific diagnostics to a file owned by this process only. Godot's global godot.log can be
    /// corrupted when two game instances append to it concurrently; the PID in this file name prevents that.
    /// </summary>
    internal sealed class LanProcessLogService : IDisposable
    {
        private static readonly Lazy<LanProcessLogService> Lazy = new(() => new LanProcessLogService());
        public static LanProcessLogService Instance => Lazy.Value;

        private readonly object _gate = new();
        private StreamWriter? _writer;
        private string? _path;
        private string _role = "unknown";
        private readonly int _processId = System.Environment.ProcessId;
        private readonly string _startedAt = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        public string? CurrentPath
        {
            get
            {
                lock (_gate)
                    return _path;
            }
        }

        private LanProcessLogService()
        {
        }

        public void Initialize()
        {
            lock (_gate)
            {
                if (_writer != null)
                    return;

                try
                {
                    var directory = Path.Combine(ProjectSettings.GlobalizePath("user://"), "lan_logs");
                    Directory.CreateDirectory(directory);
                    _path = Path.Combine(directory, $"lan_{_processId}_{_startedAt}.log");
                    _writer = OpenWriter(_path);
                    WriteLineLocked("INFO", $"LAN process log started. pid={_processId} role={_role}");
                }
                catch (Exception exception)
                {
                    GD.PushWarning($"[LAN Multiplayer][ProcessLog] Could not create independent log: {exception}");
                    _writer = null;
                    _path = null;
                }
            }
        }

        public void SetRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return;

            lock (_gate)
            {
                Initialize();
                role = SanitizeRole(role);
                if (string.Equals(_role, role, StringComparison.OrdinalIgnoreCase))
                    return;

                _role = role;
                TryRenameForRoleLocked();
                WriteLineLocked("INFO", $"Process role set to {_role}.");
            }
        }

        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message) => Write("ERROR", message);

        private void Write(string level, string message)
        {
            lock (_gate)
            {
                Initialize();
                WriteLineLocked(level, message);
            }
        }

        private void WriteLineLocked(string level, string message)
        {
            if (_writer == null)
                return;

            var normalized = message.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
                _writer.WriteLine($"{DateTime.Now:O} [{level}] [pid={_processId}] [role={_role}] {line}");
            _writer.Flush();
        }

        private void TryRenameForRoleLocked()
        {
            if (_writer == null || string.IsNullOrWhiteSpace(_path))
                return;

            try
            {
                var directory = Path.GetDirectoryName(_path)!;
                var desiredPath = Path.Combine(directory, $"{_role}_{_processId}_{_startedAt}.log");
                if (string.Equals(_path, desiredPath, StringComparison.OrdinalIgnoreCase))
                    return;

                _writer.Flush();
                _writer.Dispose();
                _writer = null;

                if (File.Exists(_path) && !File.Exists(desiredPath))
                    File.Move(_path, desiredPath);

                _path = File.Exists(desiredPath) ? desiredPath : _path;
                _writer = OpenWriter(_path);
            }
            catch (Exception exception)
            {
                GD.PushWarning($"[LAN Multiplayer][ProcessLog] Could not rename role log: {exception.Message}");
                if (_writer == null && !string.IsNullOrWhiteSpace(_path))
                {
                    try
                    {
                        _writer = OpenWriter(_path);
                    }
                    catch
                    {
                        // Logging is diagnostic-only; never destabilize the game if the file is unavailable.
                    }
                }
            }
        }

        private static StreamWriter OpenWriter(string path)
        {
            return new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }

        private static string SanitizeRole(string role)
        {
            var chars = role.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            return new string(chars);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
