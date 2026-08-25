using Godot;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Discovery;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Components;

internal partial class NearbyLobbyPanel : VBoxContainer
{
    private readonly LanDiscoveryClient _discovery = new();
    private VBoxContainer? _roomList;
    private Label? _status;
    private Action<DiscoveredRoom>? _joinRoom;
    private double _refreshTimer;
    private string _fingerprint = string.Empty;

    public static NearbyLobbyPanel Create(Action<DiscoveredRoom> joinRoom)
    {
        return new NearbyLobbyPanel
        {
            Name = "NearbyLobbyPanel",
            CustomMinimumSize = new Vector2(332, 350),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            _joinRoom = joinRoom
        };
    }

    public override void _Ready()
    {
        base._Ready();
        AddThemeConstantOverride("separation", 8);

        _status = new Label
        {
            Name = "DiscoveryStatus",
            Text = "Searching for nearby LAN rooms…",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 30),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_status);

        var scroll = new ScrollContainer
        {
            Name = "RoomScroll",
            CustomMinimumSize = new Vector2(332, 310),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        AddChild(scroll);

        _roomList = new VBoxContainer
        {
            Name = "Rooms",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _roomList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_roomList);

        if (!_discovery.Start())
            _status.Text = "LAN discovery unavailable — manual address still works";
        SetProcess(true);
        RefreshRooms(force: true);
    }

    public override void _Process(double delta)
    {
        _refreshTimer += delta;
        if (_refreshTimer < 0.35)
            return;

        _refreshTimer = 0;
        RefreshRooms(force: false);
    }

    public override void _ExitTree()
    {
        _discovery.Dispose();
        base._ExitTree();
    }

    private void RefreshRooms(bool force)
    {
        if (_roomList == null || _status == null)
            return;

        var rooms = _discovery.GetRooms();
        var fingerprint = string.Join('|', rooms.Select(room =>
            $"{room.HostId:N}:{room.CurrentPlayers}:{room.AcceptingPlayers}:" +
            $"{Math.Round(room.LatencyMs)}:{Math.Round(room.PacketLossPercent)}"));
        if (!force && fingerprint == _fingerprint)
            return;

        _fingerprint = fingerprint;
        foreach (var child in _roomList.GetChildren())
        {
            _roomList.RemoveChild(child);
            child.QueueFree();
        }

        if (rooms.Count == 0)
        {
            _status.Text = "Searching for nearby LAN rooms…";
            return;
        }

        _status.Text = rooms.Count == 1 ? "1 nearby LAN room" : $"{rooms.Count} nearby LAN rooms";
        foreach (var room in rooms)
            _roomList.AddChild(CreateRoomButton(room));
    }

    private Button CreateRoomButton(DiscoveredRoom room)
    {
        var quality = QualityText(room.Quality);
        var button = new Button
        {
            Name = $"Room_{room.HostId:N}",
            Text = $"{quality.Icon}  {room.HostName}  ·  {room.CurrentPlayers}/{room.MaxPlayers}\n" +
                   $"{room.GameMode}  ·  {room.LatencyMs:0} ms  ·  {room.PacketLossPercent:0.#}%  ·  {quality.Name}",
            TooltipText = $"{room.Address}:{room.GamePort}\n" +
                          $"Channel: {room.GameChannel}  ·  LAN Mod: {room.ModVersion}",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(320, 72),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Disabled = !room.AcceptingPlayers || room.CurrentPlayers >= room.MaxPlayers,
            SelfModulate = quality.Color
        };
        button.Pressed += () => _joinRoom?.Invoke(room);
        return button;
    }

    private static (string Icon, string Name, Color Color) QualityText(ConnectionQuality quality)
    {
        return quality switch
        {
            ConnectionQuality.Excellent => ("●", "Excellent", new Color(0.78f, 1f, 0.82f)),
            ConnectionQuality.Good => ("●", "Good", new Color(0.88f, 1f, 0.9f)),
            ConnectionQuality.Fair => ("●", "Fair", new Color(1f, 0.93f, 0.7f)),
            _ => ("●", "Poor", new Color(1f, 0.72f, 0.68f))
        };
    }
}
