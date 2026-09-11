using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MeshUp.Maui.Models;

/// <summary>
/// A friendly, non-technical representation of a mesh node that we can chat with.
/// Hides Meshtastic concepts like node numbers/channel indexes from the UI.
/// </summary>
public class Contact : INotifyPropertyChanged
{
    private string? _shortName;
    private string? _longName;
    private string? _displayNameOverride;
    private string? _lastMessagePreview;
    private DateTimeOffset? _lastMessageAt;
    private bool _isFavourite;

    public Contact(uint nodeNum)
    {
        NodeNum = nodeNum;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The underlying Meshtastic node number. Not shown in the UI.</summary>
    public uint NodeNum { get; }

    /// <summary>Short display name reported by the radio (e.g. "ABCD"). Falls back to the node number if unset.</summary>
    public string? ShortName
    {
        get => _shortName;
        set => SetField(ref _shortName, value, nameof(ShortName), nameof(DisplayName), nameof(Initials));
    }

    /// <summary>Full display name reported by the radio, used as a secondary/subtitle where useful.</summary>
    public string? LongName
    {
        get => _longName;
        set => SetField(ref _longName, value, nameof(LongName), nameof(DisplayName), nameof(Initials));
    }

    /// <summary>
    /// A friendly name resolved from the phone's Contacts list (matched by email address against
    /// this node's reported name), if any. Takes priority over <see cref="ShortName"/>/<see cref="LongName"/>
    /// when present.
    /// </summary>
    public string? DisplayNameOverride
    {
        get => _displayNameOverride;
        set => SetField(ref _displayNameOverride, value, nameof(DisplayNameOverride), nameof(DisplayName), nameof(Initials));
    }

    /// <summary>The most recent message text exchanged with this contact, for the chat list preview.</summary>
    public string? LastMessagePreview
    {
        get => _lastMessagePreview;
        set => SetField(ref _lastMessagePreview, value, nameof(LastMessagePreview));
    }

    /// <summary>Timestamp of the most recent message exchanged with this contact.</summary>
    public DateTimeOffset? LastMessageAt
    {
        get => _lastMessageAt;
        set => SetField(ref _lastMessageAt, value, nameof(LastMessageAt));
    }

    /// <summary>Whether the user has "liked"/favourited this chat, pinning it near the top of the chat list.</summary>
    public bool IsFavourite
    {
        get => _isFavourite;
        set => SetField(ref _isFavourite, value, nameof(IsFavourite));
    }

    /// <summary>Friendly display name for the contact, never showing raw node numbers unless nothing else is known.</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(DisplayNameOverride)
        ? DisplayNameOverride
        : !string.IsNullOrWhiteSpace(ShortName)
            ? ShortName
            : !string.IsNullOrWhiteSpace(LongName)
                ? LongName
                : $"Node {NodeNum}";

    /// <summary>One or two character initials derived from the display name, for use in avatar badges.</summary>
    public string Initials
    {
        get
        {
            var name = DisplayName.Trim();
            if (name.Length == 0)
            {
                return "?";
            }

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            }

            return name.Length >= 2
                ? name[..2].ToUpperInvariant()
                : name[..1].ToUpperInvariant();
        }
    }

    private void SetField<T>(ref T field, T value, params string[] propertyNames)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
