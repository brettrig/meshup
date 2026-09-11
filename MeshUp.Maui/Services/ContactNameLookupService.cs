using Microsoft.Maui.ApplicationModel.Communication;

namespace MeshUp.Maui.Services;

/// <summary>
/// Looks up phone contacts (via the platform Contacts list) by email address, so a mesh node's
/// reported name can be matched against a contact's email to show their real name in the chat UI.
/// Loads/caches the phone's contacts once per app session; gracefully does nothing if the
/// contacts permission is denied or unavailable on the current platform.
/// </summary>
public class ContactNameLookupService
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private Dictionary<string, string>? _nameByEmail;
    private bool _permissionDenied;

    /// <summary>
    /// Returns the phone contact's display name whose email address exactly matches
    /// <paramref name="deviceName"/> (case-insensitive), or null if there's no match, the
    /// contacts permission was denied, or the lookup otherwise failed.
    /// </summary>
    public async Task<string?> TryFindNameByEmailAsync(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        var index = await GetOrLoadIndexAsync();
        if (index is null)
        {
            return null;
        }

        return index.TryGetValue(deviceName.Trim(), out var name) ? name : null;
    }

    private async Task<Dictionary<string, string>?> GetOrLoadIndexAsync()
    {
        if (_nameByEmail is not null || _permissionDenied)
        {
            return _nameByEmail;
        }

        await _loadGate.WaitAsync();
        try
        {
            if (_nameByEmail is not null || _permissionDenied)
            {
                return _nameByEmail;
            }

            // Permissions.CheckStatusAsync/RequestAsync must be invoked from the UI thread on most
            // platforms. This method is often reached from a background BLE callback (via
            // MeshtasticChatService.OnNodeUpdated), so explicitly marshal onto the main thread -
            // otherwise the request throws, gets swallowed below, and silently disables this
            // feature for the rest of the app session.
            var status = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var current = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
                if (current != PermissionStatus.Granted)
                {
                    current = await Permissions.RequestAsync<Permissions.ContactsRead>();
                }

                return current;
            });

            if (status != PermissionStatus.Granted)
            {
                // The user (or platform policy) explicitly denied access - don't keep re-prompting.
                _permissionDenied = true;
                return null;
            }

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var contacts = await Contacts.GetAllAsync();
            if (contacts is not null)
            {
                foreach (var contact in contacts)
                {
                    var displayName = contact.DisplayName;
                    if (string.IsNullOrWhiteSpace(displayName) || contact.Emails is null)
                    {
                        continue;
                    }

                    foreach (var email in contact.Emails)
                    {
                        if (!string.IsNullOrWhiteSpace(email.EmailAddress))
                        {
                            index[email.EmailAddress.Trim()] = displayName;
                        }
                    }
                }
            }

            _nameByEmail = index;
            return _nameByEmail;
        }
        catch
        {
            // Unexpected/transient failure (e.g. platform API hiccup) - degrade gracefully for this
            // attempt, but don't permanently disable the feature; a later call may succeed.
            return null;
        }
        finally
        {
            _loadGate.Release();
        }
    }
}
