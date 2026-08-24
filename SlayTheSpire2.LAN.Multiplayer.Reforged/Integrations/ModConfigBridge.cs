using System.Globalization;
using System.Net;
using System.Reflection;
using Godot;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Components;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Models;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Compatibility;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Integrations
{
    /// <summary>
    /// Optional, zero-reference bridge to ModConfig.
    /// LAN Multiplayer keeps lan_settings.json as its runtime fallback, while
    /// ModConfig supplies the UI and its own persisted values when installed.
    /// </summary>
    internal static class ModConfigBridge
    {
        private const string ModId = "SlayTheSpire2.LAN.Multiplayer.Reforged";
        private const int MaxDetectionAttempts = 120;

        internal const string DefaultAddressKey = "defaultAddress";

        private const string HostPortKey = "hostPort";
        private const string HostMaxPlayersKey = "hostMaxPlayers";
        private const string ConnectTimeoutKey = "connectTimeoutSeconds";
        private const string RememberAddressKey = "rememberJoinAddress";
        private const string PlayerNameKey = "playerName";
        private const string NetIdKey = "netId";

        private static bool _available;
        private static bool _registered;
        private static int _detectionAttempts;

        private static Type? _apiType;
        private static Type? _entryType;
        private static Type? _configTypeEnum;

        internal static bool IsAvailable => _available;

        internal static void DeferredRegister()
        {
            Detect();
            ScheduleNextFrame();
        }

        private static void ScheduleNextFrame()
        {
            if (_registered || _detectionAttempts >= MaxDetectionAttempts)
                return;

            if (Engine.GetMainLoop() is not SceneTree tree)
                return;

            tree.ProcessFrame -= OnNextFrame;
            tree.ProcessFrame += OnNextFrame;
        }

        private static void OnNextFrame()
        {
            if (Engine.GetMainLoop() is SceneTree tree)
                tree.ProcessFrame -= OnNextFrame;

            if (_registered)
                return;

            _detectionAttempts++;
            Detect();

            if (_available)
                Register();
            else
                ScheduleNextFrame();
        }

        private static void Detect()
        {
            try
            {
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(assembly =>
                    {
                        try { return assembly.GetTypes(); }
                        catch { return Type.EmptyTypes; }
                    })
                    .ToArray();

                _apiType = allTypes.FirstOrDefault(type => type.FullName == "ModConfig.ModConfigApi");
                _entryType = allTypes.FirstOrDefault(type => type.FullName == "ModConfig.ConfigEntry");
                _configTypeEnum = allTypes.FirstOrDefault(type => type.FullName == "ModConfig.ConfigType");

                _available = _apiType != null && _entryType != null && _configTypeEnum != null;
            }
            catch
            {
                _available = false;
            }
        }

        private static void Register()
        {
            if (_registered || !_available)
                return;

            try
            {
                var entries = BuildEntries();
                var displayNames = new Dictionary<string, string>
                {
                    ["en"] = "LAN Multiplayer",
                    ["zhs"] = "局域网联机"
                };

                var registerMethod = _apiType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(method => method.Name == "Register")
                    .OrderByDescending(method => method.GetParameters().Length)
                    .First();

                if (registerMethod.GetParameters().Length == 4)
                {
                    registerMethod.Invoke(null, new object[]
                    {
                        ModId, displayNames["en"], displayNames, entries
                    });
                }
                else
                {
                    registerMethod.Invoke(null, new object[]
                    {
                        ModId, displayNames["en"], entries
                    });
                }

                _registered = true;
                ApplyPersistedValues();
                GD.Print("[SlayTheSpire2.LAN.Multiplayer.Reforged] Registered with ModConfig.");
            }
            catch (Exception exception)
            {
                GD.PrintErr($"[SlayTheSpire2.LAN.Multiplayer.Reforged] ModConfig registration failed: {exception}");
            }
        }

        private static Array BuildEntries()
        {
            var settings = SettingsService.Instance.SettingsModel;
            var entries = new List<object>();

            entries.Add(Entry(config =>
            {
                Set(config, "Label", "Connection");
                Set(config, "Labels", L("Connection", "连接设置"));
                Set(config, "Type", EnumValue("Header"));
            }));

            entries.Add(Entry(config =>
            {
                Set(config, "Key", HostPortKey);
                Set(config, "Label", "Default LAN port");
                Set(config, "Labels", L("Default LAN port", "默认局域网端口"));
                Set(config, "Type", EnumValue("TextInput"));
                Set(config, "DefaultValue", (object)settings.HostPort.ToString(CultureInfo.InvariantCulture));
                Set(config, "MaxLength", 5);
                Set(config, "Placeholder", "33771");
                Set(config, "Validator", new Func<object, bool>(value => TryParsePort(ToText(value), out _)));
                Set(config, "Description", "Used when hosting and when a join address does not contain a port.");
                Set(config, "Descriptions", L(
                    "Used when hosting and when a join address does not contain a port.",
                    "用于创建主机；加入地址未填写端口时也会使用此端口。"));
                Set(config, "OnChanged", new Action<object>(OnHostPortChanged));
            }));

            entries.Add(Entry(config =>
            {
                Set(config, "Key", HostMaxPlayersKey);
                Set(config, "Label", "Maximum players");
                Set(config, "Labels", L("Maximum players", "最大玩家数"));
                Set(config, "Type", EnumValue("Slider"));
                Set(config, "DefaultValue", (object)(float)LanProtocolPolicy.ClampPlayerCount(settings.HostMaxPlayers));
                Set(config, "Min", (float)LanProtocolPolicy.MinPlayers);
                Set(config, "Max", (float)LanProtocolPolicy.MaxPlayers);
                Set(config, "Step", 1f);
                Set(config, "Format", "F0");
                Set(config, "Description", "Maximum number of players in a hosted LAN lobby.");
                Set(config, "Descriptions", L(
                    "Maximum number of players in a hosted LAN lobby.",
                    "创建局域网房间时允许进入的最大玩家数。"));
                Set(config, "OnChanged", new Action<object>(OnHostMaxPlayersChanged));
            }));

            entries.Add(Entry(config =>
            {
                Set(config, "Key", ConnectTimeoutKey);
                Set(config, "Label", "Connection timeout");
                Set(config, "Labels", L("Connection timeout", "连接超时"));
                Set(config, "Type", EnumValue("Slider"));
                Set(config, "DefaultValue", (object)(float)Math.Clamp(settings.ConnectTimeoutSeconds, 3, 60));
                Set(config, "Min", 3f);
                Set(config, "Max", 60f);
                Set(config, "Step", 1f);
                Set(config, "Format", "F0");
                Set(config, "Description", "Seconds allowed for the ENet connection and handshake.");
                Set(config, "Descriptions", L(
                    "Seconds allowed for the ENet connection and handshake.",
                    "等待 ENet 建立连接及完成握手的最长秒数。"));
                Set(config, "OnChanged", new Action<object>(OnConnectTimeoutChanged));
            }));

            entries.Add(Entry(config =>
            {
                Set(config, "Key", DefaultAddressKey);
                Set(config, "Label", "Default server address");
                Set(config, "Labels", L("Default server address", "默认服务器地址"));
                Set(config, "Type", EnumValue("TextInput"));
                Set(config, "DefaultValue", (object)settings.IPAddress);
                Set(config, "MaxLength", 128);
                Set(config, "Placeholder", "192.168.1.100:33771");
                Set(config, "Validator", new Func<object, bool>(value => IsValidAddress(ToText(value))));
                Set(config, "Description",
                    "Initial address shown on the LAN join screen. An explicit :port overrides the default LAN port.");
                Set(config, "Descriptions", L(
                    "Initial address shown on the LAN join screen. An explicit :port overrides the default LAN port.",
                    "局域网加入界面默认显示的地址；地址中显式填写的端口会覆盖默认端口。"));
                Set(config, "OnChanged", new Action<object>(OnDefaultAddressChanged));
            }));

            entries.Add(Entry(config =>
            {
                Set(config, "Key", RememberAddressKey);
                Set(config, "Label", "Remember last server");
                Set(config, "Labels", L("Remember last server", "记住上次服务器"));
                Set(config, "Type", EnumValue("Toggle"));
                Set(config, "DefaultValue", (object)settings.RememberJoinAddress);
                Set(config, "Description", "Save the submitted join address as the next default.");
                Set(config, "Descriptions", L(
                    "Save the submitted join address as the next default.",
                    "提交有效加入地址后，将其保存为下次打开界面时的默认地址。"));
                Set(config, "OnChanged", new Action<object>(OnRememberAddressChanged));
            }));

            entries.Add(Entry(config => Set(config, "Type", EnumValue("Separator"))));

            entries.Add(Entry(config =>
            {
                Set(config, "Label", "Identity");
                Set(config, "Labels", L("Identity", "身份设置"));
                Set(config, "Type", EnumValue("Header"));
            }));

            entries.Add(Entry(config =>
            {
                Set(config, "Key", PlayerNameKey);
                Set(config, "Label", "Player name");
                Set(config, "Labels", L("Player name", "玩家名称"));
                Set(config, "Type", EnumValue("TextInput"));
                Set(config, "DefaultValue", (object)settings.PlayerName);
                Set(config, "MaxLength", 16);
                Set(config, "Placeholder", "Player");
                Set(config, "Validator", new Func<object, bool>(value =>
                    !PlayerNameLineEdit.GetPlayerNameIsInvalid(ToText(value))));
                Set(config, "Description", "Name displayed to other LAN players.");
                Set(config, "Descriptions", L(
                    "Name displayed to other LAN players.",
                    "向其他局域网玩家显示的名称。"));
                Set(config, "OnChanged", new Action<object>(OnPlayerNameChanged));
            }));

            entries.Add(Entry(config =>
            {
                Set(config, "Key", NetIdKey);
                Set(config, "Label", "Preferred Net ID");
                Set(config, "Labels", L("Preferred Net ID", "首选网络 ID"));
                Set(config, "Type", EnumValue("TextInput"));
                Set(config, "DefaultValue", (object)settings.NetId.ToString(CultureInfo.InvariantCulture));
                Set(config, "MaxLength", 20);
                Set(config, "Placeholder", "1000");
                Set(config, "Validator", new Func<object, bool>(value => TryParseNetId(ToText(value), out _)));
                Set(config, "Description",
                    "Preferred local player ID. It must be at least 2 and should be unique per client; the host can resolve collisions automatically.");
                Set(config, "Descriptions", L(
                    "Preferred local player ID. It must be at least 2 and should be unique per client; the host can resolve collisions automatically.",
                    "本机首选玩家 ID，必须不小于 2，且各客户端最好不同；发生冲突时主机可自动重新分配。"));
                Set(config, "OnChanged", new Action<object>(OnNetIdChanged));
            }));

            var typedArray = Array.CreateInstance(_entryType!, entries.Count);
            for (var index = 0; index < entries.Count; index++)
                typedArray.SetValue(entries[index], index);

            return typedArray;
        }

        private static void ApplyPersistedValues()
        {
            var service = SettingsService.Instance;
            var settings = service.SettingsModel;

            var portText = GetValue(HostPortKey, settings.HostPort.ToString(CultureInfo.InvariantCulture));
            if (TryParsePort(portText, out var port))
                settings.HostPort = port;

            settings.HostMaxPlayers = LanProtocolPolicy.ClampPlayerCount(
                (int)MathF.Round(GetValue(HostMaxPlayersKey, (float)settings.HostMaxPlayers)));

            settings.ConnectTimeoutSeconds = Math.Clamp(
                (int)MathF.Round(GetValue(ConnectTimeoutKey, (float)settings.ConnectTimeoutSeconds)),
                3, 60);

            var address = GetValue(DefaultAddressKey, settings.IPAddress);
            if (IsValidAddress(address))
                settings.IPAddress = address;

            settings.RememberJoinAddress = GetValue(
                RememberAddressKey,
                settings.RememberJoinAddress);

            var playerName = GetValue(PlayerNameKey, settings.PlayerName);
            if (!PlayerNameLineEdit.GetPlayerNameIsInvalid(playerName))
                settings.PlayerName = playerName;

            var netIdText = GetValue(NetIdKey, settings.NetId.ToString(CultureInfo.InvariantCulture));
            if (TryParseNetId(netIdText, out var netId))
                settings.NetId = netId;

            service.WriteSettings();
            LanPlayerNameService.Instance.SetHostPlayerName();
        }

        internal static void SetValue(string key, object value)
        {
            if (!_available || !_registered)
                return;

            try
            {
                _apiType!.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, new[] { (object)ModId, key, value });
            }
            catch (Exception exception)
            {
                GD.PrintErr(
                    $"[SlayTheSpire2.LAN.Multiplayer.Reforged] Failed to sync ModConfig value {key}: {exception}");
            }
        }

        private static T GetValue<T>(string key, T fallback)
        {
            if (!_available || !_registered)
                return fallback;

            try
            {
                var method = _apiType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(candidate =>
                        candidate.Name == "GetValue" &&
                        candidate.IsGenericMethodDefinition);

                var result = method?.MakeGenericMethod(typeof(T))
                    .Invoke(null, new object[] { ModId, key });

                return result is T typed ? typed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static void OnHostPortChanged(object value)
        {
            if (!TryParsePort(ToText(value), out var port))
                return;

            var service = SettingsService.Instance;
            service.SettingsModel.HostPort = port;
            service.WriteSettings();
        }

        private static void OnHostMaxPlayersChanged(object value)
        {
            var maxPlayers = LanProtocolPolicy.ClampPlayerCount(
                (int)MathF.Round(Convert.ToSingle(value, CultureInfo.InvariantCulture)));

            var service = SettingsService.Instance;
            service.SettingsModel.HostMaxPlayers = maxPlayers;
            service.WriteSettings();
        }

        private static void OnConnectTimeoutChanged(object value)
        {
            var timeout = Math.Clamp(
                (int)MathF.Round(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
                3, 60);

            var service = SettingsService.Instance;
            service.SettingsModel.ConnectTimeoutSeconds = timeout;
            service.WriteSettings();
        }

        private static void OnDefaultAddressChanged(object value)
        {
            var address = ToText(value);
            if (!IsValidAddress(address))
                return;

            var service = SettingsService.Instance;
            service.SettingsModel.IPAddress = address;
            service.WriteSettings();
        }

        private static void OnRememberAddressChanged(object value)
        {
            var service = SettingsService.Instance;
            service.SettingsModel.RememberJoinAddress =
                Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            service.WriteSettings();
        }

        private static void OnPlayerNameChanged(object value)
        {
            var playerName = ToText(value);
            if (PlayerNameLineEdit.GetPlayerNameIsInvalid(playerName))
                return;

            var service = SettingsService.Instance;
            service.SettingsModel.PlayerName = playerName;
            service.WriteSettings();
            LanPlayerNameService.Instance.SetHostPlayerName();
        }

        private static void OnNetIdChanged(object value)
        {
            if (!TryParseNetId(ToText(value), out var netId))
                return;

            var service = SettingsService.Instance;
            service.SettingsModel.NetId = netId;
            service.WriteSettings();
        }

        private static bool TryParsePort(string text, out ushort port)
        {
            return ushort.TryParse(
                       text, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
                   port > 0;
        }

        private static bool TryParseNetId(string text, out ulong netId)
        {
            return ulong.TryParse(
                       text, NumberStyles.None, CultureInfo.InvariantCulture, out netId) &&
                   netId >= 2;
        }

        private static bool IsValidAddress(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (text == "localhost")
                return true;

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https")
            {
                return !string.IsNullOrWhiteSpace(uri.Host) &&
                       uri.Port is >= -1 and <= ushort.MaxValue;
            }

            return IPAddress.TryParse(text, out _) ||
                   IPEndPoint.TryParse(text, out _);
        }

        private static string ToText(object? value)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ??
                   string.Empty;
        }

        private static object Entry(Action<object> configure)
        {
            var instance = Activator.CreateInstance(_entryType!)!;
            configure(instance);
            return instance;
        }

        private static void Set(object target, string propertyName, object value)
        {
            target.GetType().GetProperty(propertyName)?.SetValue(target, value);
        }

        private static Dictionary<string, string> L(string english, string simplifiedChinese)
        {
            return new Dictionary<string, string>
            {
                ["en"] = english,
                ["zhs"] = simplifiedChinese
            };
        }

        private static object EnumValue(string name)
        {
            return Enum.Parse(_configTypeEnum!, name);
        }
    }
}
