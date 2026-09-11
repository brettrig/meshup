using Google.Protobuf;
using Meshtastic.Protobufs;
using Plugin.BLE;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;

namespace MeshUp.Maui.Services;

/// <summary>
/// Handles BLE discovery, pairing/connection, and basic Meshtastic protocol
/// communication (reading the device's MyNodeInfo via the FromRadio characteristic).
/// </summary>
public class MeshtasticBleService
{
    /// <summary>
    /// Reserved node number meaning "broadcast to everyone on the channel".
    /// </summary>
    public const uint BroadcastNodeNum = 0xFFFFFFFF;

    // Meshtastic BLE GATT service/characteristic UUIDs (fixed, documented by the Meshtastic firmware).
    public static readonly Guid MeshtasticServiceUuid = Guid.Parse("6ba1b218-15a8-461f-9fa8-5dcae273eafd");
    private static readonly Guid ToRadioCharacteristicUuid = Guid.Parse("f75c76d2-129e-4dad-a1dd-7866124401e7");
    private static readonly Guid FromRadioCharacteristicUuid = Guid.Parse("2c55e69e-4993-11ed-b878-0242ac120002");
    private static readonly Guid FromNumCharacteristicUuid = Guid.Parse("ed9da18c-a800-4f66-a670-aa7547e34453");

    // Preference key used to remember the last device we successfully connected to,
    // so the app can automatically reconnect to it after being closed and reopened.
    private const string LastDeviceIdPreferenceKey = "Meshtastic.LastConnectedDeviceId";

    // The largest ATT MTU we'll ask for. 517 is the maximum allowed by the BLE spec (512-byte
    // attribute value + 5-byte ATT header) and comfortably covers Meshtastic's protobuf packets,
    // including replies with a quoted line prepended.
    private const int MaxRequestedMtu = 517;

    private readonly IBluetoothLE _bluetoothLe;
    private readonly IAdapter _adapter;
    private readonly IForegroundConnectionService _foregroundConnectionService;

    private IDevice? _connectedDevice;
    private ICharacteristic? _fromRadioCharacteristic;
    private ICharacteristic? _toRadioCharacteristic;
    private ICharacteristic? _fromNumCharacteristic;

    // Android (and BLE in general) only allows a single outstanding GATT operation
    // (read/write/notification-subscribe) per connection at a time. Overlapping calls
    // cause native readCharacteristic/writeCharacteristic calls to fail immediately,
    // surfacing as CharacteristicReadException/CharacteristicWriteException. All GATT
    // access on the connected device must go through AccessGattAsync to serialize it.
    private readonly SemaphoreSlim _gattGate = new(1, 1);

    /// <summary>True if currently connected to a Meshtastic device.</summary>
    public bool IsConnected => _connectedDevice is not null;

    public event EventHandler<IDevice>? DeviceDiscovered;
    public event EventHandler<MyNodeInfo>? NodeInfoReceived;

    /// <summary>Raised whenever the radio reports info (name, position, etc.) for any node in its DB, including itself and other mesh members.</summary>
    public event EventHandler<NodeInfo>? NodeUpdated;

    /// <summary>Raised whenever a text message (TEXT_MESSAGE_APP) is received from the mesh.</summary>
    public event EventHandler<MeshtasticTextMessage>? TextMessageReceived;

    /// <summary>Raised whenever the radio reports one of its configured channels.</summary>
    public event EventHandler<Channel>? ChannelReceived;

    /// <summary>Raised when the radio finishes streaming the initial config/node DB in response to want_config_id.</summary>
    public event EventHandler? ConfigComplete;

    public MeshtasticBleService(IForegroundConnectionService foregroundConnectionService)
    {
        _bluetoothLe = CrossBluetoothLE.Current;
        _adapter = _bluetoothLe.Adapter;
        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        _foregroundConnectionService = foregroundConnectionService;
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        DeviceDiscovered?.Invoke(this, e.Device);
    }

    /// <summary>
    /// Scans for nearby BLE devices advertising the Meshtastic GATT service.
    /// </summary>
    public async Task ScanForDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (_adapter.IsScanning)
        {
            await _adapter.StopScanningForDevicesAsync();
        }

        _adapter.ScanMode = ScanMode.LowLatency;
        _adapter.ScanTimeout = 15000;

        var scanFilterOptions = new ScanFilterOptions
        {
            ServiceUuids = [MeshtasticServiceUuid]
        };

        await _adapter.StartScanningForDevicesAsync(scanFilterOptions, cancellationToken: cancellationToken);
    }

    public Task StopScanAsync() => _adapter.StopScanningForDevicesAsync();

    /// <summary>
    /// Connects (pairs, if required by the OS) to the given Meshtastic device and
    /// discovers the ToRadio/FromRadio/FromNum characteristics.
    /// </summary>
    /// <exception cref="MeshtasticPairingRequiredException">
    /// Thrown when the OS reports the device requires bonding (a PIN pairing prompt)
    /// before the connection can be established. This happens only if the Meshtastic
    /// device's Bluetooth security mode is set to something other than "No PIN".
    /// </exception>
    public async Task<bool> ConnectAsync(IDevice device, CancellationToken cancellationToken = default)
    {
        try
        {
            await _adapter.ConnectToDeviceAsync(device, new ConnectParameters(forceBleTransport: true), cancellationToken);
        }
        catch (DeviceConnectionException ex)
        {
            if (IsBondingFailure(device, ex))
            {
                throw new MeshtasticPairingRequiredException(
                    "The device requires Bluetooth pairing (a PIN code) before it can be used. " +
                    "Check for a native Bluetooth pairing prompt, or confirm the PIN shown on the device/app.",
                    ex);
            }

            throw;
        }

        return await SetUpConnectedDeviceAsync(device);
    }

    /// <summary>
    /// Attempts to reconnect to the device we were last successfully connected to (identified by
    /// the platform's Bluetooth device id, saved via <see cref="LastDeviceIdPreferenceKey"/>).
    /// Returns false (without throwing) if there is no remembered device, or if reconnecting fails
    /// for any reason (e.g. the device is out of range or turned off) - callers can fall back to
    /// showing the manual setup/scan flow in that case.
    /// </summary>
    public async Task<bool> TryReconnectToLastDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return true;
        }

        var savedId = Preferences.Default.Get(LastDeviceIdPreferenceKey, string.Empty);
        if (string.IsNullOrEmpty(savedId) || !Guid.TryParse(savedId, out var deviceId))
        {
            return false;
        }

        try
        {
            var device = await _adapter.ConnectToKnownDeviceAsync(deviceId, new ConnectParameters(forceBleTransport: true), cancellationToken);
            return await SetUpConnectedDeviceAsync(device);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Discovers the Meshtastic GATT service/characteristics on an already-connected device and,
    /// if successful, remembers it as the last connected device for future auto-reconnect attempts.
    /// </summary>
    private async Task<bool> SetUpConnectedDeviceAsync(IDevice device)
    {
        // Without negotiating a larger ATT MTU, some Android devices default to a very small one
        // (as low as 20 bytes of usable payload), which is enough for short messages but can make
        // longer ones (e.g. replies, which prepend a quoted line) fail outright with a GATT write
        // error. Request a larger MTU up front; this is a no-op/best-effort on platforms or devices
        // that don't support/need it (e.g. iOS negotiates automatically), so failures are ignored.
        try
        {
            await device.RequestMtuAsync(MaxRequestedMtu);
        }
        catch
        {
            // Best-effort only - fall back to whatever MTU is already in place.
        }

        var service = await device.GetServiceAsync(MeshtasticServiceUuid);
        if (service is null)
        {
            return false;
        }

        _toRadioCharacteristic = await service.GetCharacteristicAsync(ToRadioCharacteristicUuid);
        _fromRadioCharacteristic = await service.GetCharacteristicAsync(FromRadioCharacteristicUuid);
        _fromNumCharacteristic = await service.GetCharacteristicAsync(FromNumCharacteristicUuid);

        if (_toRadioCharacteristic is null || _fromRadioCharacteristic is null)
        {
            return false;
        }

        _connectedDevice = device;

        if (_fromNumCharacteristic is not null)
        {
            _fromNumCharacteristic.ValueUpdated += OnFromNumValueUpdated;
            await AccessGattAsync(() => _fromNumCharacteristic.StartUpdatesAsync());
        }

        Preferences.Default.Set(LastDeviceIdPreferenceKey, device.Id.ToString());

        // Keep the BLE connection alive while backgrounded/locked (on Android, this starts a
        // foreground service; on other platforms it's a no-op).
        _foregroundConnectionService.Start();

        return true;
    }

    private async void OnFromNumValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        // The radio notifies FromNum whenever a new packet is available on FromRadio.
        // Drain everything currently queued (there may be more than one packet backed up).
        // This runs on a notification callback and can race with writes/reads issued by
        // RequestNodeInfoAsync (or other callers), so all GATT access is serialized via
        // _gattGate/AccessGattAsync.
        while (true)
        {
            var fromRadio = await ReadNextFromRadioAsync();
            if (fromRadio is null)
            {
                break;
            }

            DispatchFromRadio(fromRadio);
        }
    }

    /// <summary>
    /// Runs a single GATT operation (read/write/notification-subscribe) while holding an
    /// exclusive gate, since the underlying BLE stack (notably Android) only supports one
    /// outstanding GATT operation per connection at a time. Also retries once on a
    /// CharacteristicReadException/CharacteristicWriteException, since these are often
    /// transient (e.g. "readCharacteristic returned FALSE") when the stack is momentarily busy.
    /// </summary>
    private async Task<T> AccessGattAsync<T>(Func<Task<T>> operation, int maxAttempts = 3)
    {
        await _gattGate.WaitAsync();
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempt < maxAttempts &&
                    ex.GetType().Namespace == "Plugin.BLE.Abstractions.Exceptions")
                {
                    await Task.Delay(150 * attempt);
                }
            }
        }
        finally
        {
            _gattGate.Release();
        }
    }

    private Task AccessGattAsync(Func<Task> operation, int maxAttempts = 3) =>
        AccessGattAsync(async () => { await operation(); return true; }, maxAttempts);

    /// <summary>
    /// Sends the initial handshake (want_config_id) and drains FromRadio packets until
    /// the radio reports config_complete_id, raising events for every packet type seen
    /// along the way (MyNodeInfo, NodeInfo, Channel, text messages, etc.).
    /// </summary>
    public async Task RequestNodeInfoAsync(CancellationToken cancellationToken = default)
    {
        if (_toRadioCharacteristic is null)
        {
            throw new InvalidOperationException("Not connected to a Meshtastic device.");
        }

        var toRadio = new ToRadio
        {
            WantConfigId = (uint)Random.Shared.Next(1, int.MaxValue)
        };

        await AccessGattAsync(() => _toRadioCharacteristic.WriteAsync(toRadio.ToByteArray(), cancellationToken));

        // Drain FromRadio until we see config_complete_id (or run out of queued messages).
        for (var i = 0; i < 200; i++)
        {
            var fromRadio = await ReadNextFromRadioAsync(cancellationToken);
            if (fromRadio is null)
            {
                break;
            }

            DispatchFromRadio(fromRadio);

            if (fromRadio.PayloadVariantCase == FromRadio.PayloadVariantOneofCase.ConfigCompleteId)
            {
                ConfigComplete?.Invoke(this, EventArgs.Empty);
                break;
            }
        }
    }

    /// <summary>
    /// Raises the appropriate typed event for a FromRadio packet's payload variant.
    /// </summary>
    private void DispatchFromRadio(FromRadio fromRadio)
    {
        switch (fromRadio.PayloadVariantCase)
        {
            case FromRadio.PayloadVariantOneofCase.MyInfo:
                NodeInfoReceived?.Invoke(this, fromRadio.MyInfo);
                break;

            case FromRadio.PayloadVariantOneofCase.NodeInfo:
                NodeUpdated?.Invoke(this, fromRadio.NodeInfo);
                break;

            case FromRadio.PayloadVariantOneofCase.Channel:
                ChannelReceived?.Invoke(this, fromRadio.Channel);
                break;

            case FromRadio.PayloadVariantOneofCase.Packet:
                DispatchMeshPacket(fromRadio.Packet);
                break;

            case FromRadio.PayloadVariantOneofCase.ConfigCompleteId:
                ConfigComplete?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// Inspects a decoded MeshPacket for application data we understand (currently just text messages)
    /// and raises the matching event. Encrypted packets we can't decode are ignored.
    /// </summary>
    private void DispatchMeshPacket(MeshPacket packet)
    {
        if (packet.PayloadVariantCase != MeshPacket.PayloadVariantOneofCase.Decoded)
        {
            return;
        }

        var data = packet.Decoded;
        if (data.Portnum == PortNum.TextMessageApp)
        {
            var text = data.Payload.ToStringUtf8();
            TextMessageReceived?.Invoke(this, new MeshtasticTextMessage(packet.From, packet.To, packet.Channel, text));
        }
    }

    /// <summary>
    /// Sends a plain-text message onto the mesh, either as a broadcast (default) or
    /// as a direct message to a specific destination node number.
    /// </summary>
    public async Task SendTextMessageAsync(string text, uint destinationNodeNum = BroadcastNodeNum, uint channelIndex = 0, CancellationToken cancellationToken = default)
    {
        if (_toRadioCharacteristic is null)
        {
            throw new InvalidOperationException("Not connected to a Meshtastic device.");
        }

        ArgumentException.ThrowIfNullOrEmpty(text);

        var data = new Data
        {
            Portnum = PortNum.TextMessageApp,
            Payload = ByteString.CopyFromUtf8(text)
        };

        var packet = new MeshPacket
        {
            To = destinationNodeNum,
            Channel = channelIndex,
            Decoded = data,
            WantAck = true,
            Id = (uint)Random.Shared.Next(1, int.MaxValue)
        };

        var toRadio = new ToRadio
        {
            Packet = packet
        };

        await AccessGattAsync(() => _toRadioCharacteristic.WriteAsync(toRadio.ToByteArray(), cancellationToken));
    }

    private async Task<FromRadio?> ReadNextFromRadioAsync(CancellationToken cancellationToken = default)
    {
        if (_fromRadioCharacteristic is null)
        {
            return null;
        }

        var (bytes, _) = await AccessGattAsync(() => _fromRadioCharacteristic.ReadAsync(cancellationToken));
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        return FromRadio.Parser.ParseFrom(bytes);
    }

    public async Task DisconnectAsync()
    {
        if (_fromNumCharacteristic is not null)
        {
            _fromNumCharacteristic.ValueUpdated -= OnFromNumValueUpdated;
        }

        if (_connectedDevice is not null)
        {
            await _adapter.DisconnectDeviceAsync(_connectedDevice);
            _connectedDevice = null;
        }

        // This was a deliberate user disconnect, so don't try to reconnect to this device automatically next launch.
        Preferences.Default.Remove(LastDeviceIdPreferenceKey);

        _foregroundConnectionService.Stop();
    }

    /// <summary>
    /// Best-effort detection of whether a connection failure was caused by the OS
    /// requiring Bluetooth bonding (PIN pairing) rather than a generic BLE error.
    /// Plugin.BLE does not expose a dedicated exception type for this, so we inspect
    /// the device's reported bond state and the exception message as a fallback.
    /// </summary>
    private static bool IsBondingFailure(IDevice device, DeviceConnectionException ex)
    {
        if (device.BondState == DeviceBondState.Bonding)
        {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("bond", StringComparison.OrdinalIgnoreCase)
            || message.Contains("pair", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient authentication", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Thrown when a Meshtastic device cannot be connected to because it requires
/// Bluetooth pairing (the device's Bluetooth security mode is not "No PIN").
/// </summary>
public class MeshtasticPairingRequiredException : Exception
{
    public MeshtasticPairingRequiredException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A decoded plain-text message received from (or sent to) the mesh via TEXT_MESSAGE_APP.
/// </summary>
/// <param name="From">The sending node number.</param>
/// <param name="To">The destination node number, or <see cref="MeshtasticBleService.BroadcastNodeNum"/> if broadcast.</param>
/// <param name="ChannelIndex">The local channel index the message was sent/received on.</param>
/// <param name="Text">The decoded message text.</param>
public record MeshtasticTextMessage(uint From, uint To, uint ChannelIndex, string Text);
