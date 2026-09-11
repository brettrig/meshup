using MeshUp.Maui.Services;
using Plugin.BLE.Abstractions.Contracts;
using System.Collections.ObjectModel;

namespace MeshUp.Maui;

/// <summary>
/// One-time (or occasional) setup screen for scanning, pairing, and connecting to a
/// Meshtastic radio over BLE. Kept separate from the chat UI so day-to-day use never
/// needs to deal with BLE device lists.
/// </summary>
public partial class SetupPage : ContentPage
{
    private readonly MeshtasticChatService _chatService;
    private readonly ObservableCollection<IDevice> _devices = new();
    private IDevice? _selectedDevice;

    public SetupPage(MeshtasticChatService chatService)
    {
        InitializeComponent();

        _chatService = chatService;
        _chatService.DeviceDiscovered += OnDeviceDiscovered;

        DevicesList.ItemsSource = _devices;

        if (_chatService.IsConnected)
        {
            StatusLabel.Text = "Already connected.";
            DisconnectButton.IsEnabled = true;
            DoneButton.IsEnabled = true;
        }
    }

    private void OnDeviceDiscovered(object? sender, IDevice device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!_devices.Any(d => d.Id == device.Id))
            {
                _devices.Add(device);
            }
        });
    }

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        try
        {
            SetBusy(true, "Requesting permissions and scanning...");
            _devices.Clear();

            var permissionStatus = await Permissions.RequestAsync<Permissions.Bluetooth>();
            if (permissionStatus != PermissionStatus.Granted)
            {
                StatusLabel.Text = "Bluetooth permission was not granted.";
                return;
            }

            await _chatService.ScanForDevicesAsync();
            StatusLabel.Text = $"Scan complete. Found {_devices.Count} radio(s).";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnDeviceSelected(object? sender, SelectionChangedEventArgs e)
    {
        _selectedDevice = e.CurrentSelection.FirstOrDefault() as IDevice;
        ConnectButton.IsEnabled = _selectedDevice is not null;
    }

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_selectedDevice is null)
        {
            return;
        }

        try
        {
            SetBusy(true, $"Connecting to {_selectedDevice.Name}...");

            var connected = await _chatService.ConnectAsync(_selectedDevice);
            if (!connected)
            {
                StatusLabel.Text = "Failed to connect or Meshtastic service not found on device.";
                return;
            }

            StatusLabel.Text = "Connected. Requesting node info...";
            ConnectButton.IsEnabled = false;
            DisconnectButton.IsEnabled = true;
            await _chatService.RequestNodeInfoAsync();
            StatusLabel.Text = "Connected and ready.";
            DoneButton.IsEnabled = true;
        }
        catch (MeshtasticPairingRequiredException ex)
        {
            StatusLabel.Text = $"Pairing required: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Connection failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDisconnectClicked(object? sender, EventArgs e)
    {
        try
        {
            SetBusy(true, "Disconnecting...");
            await _chatService.DisconnectAsync();
            StatusLabel.Text = "Disconnected.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Disconnect failed: {ex.Message}";
        }
        finally
        {
            DisconnectButton.IsEnabled = false;
            DoneButton.IsEnabled = false;
            ConnectButton.IsEnabled = _selectedDevice is not null;
            SetBusy(false);
        }
    }

    private async void OnDoneClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//ChatsListPage");
    }

    private void SetBusy(bool busy, string? message = null)
    {
        BusyIndicator.IsRunning = busy;
        BusyIndicator.IsVisible = busy;
        ScanButton.IsEnabled = !busy;
        ConnectButton.IsEnabled = !busy && _selectedDevice is not null;

        if (message is not null)
        {
            StatusLabel.Text = message;
        }
    }
}
