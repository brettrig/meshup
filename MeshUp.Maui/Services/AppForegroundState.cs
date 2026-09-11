namespace MeshUp.Maui.Services;

/// <summary>
/// Tracks whether the app is currently in the foreground and, if so, which chat thread
/// (the "Everyone" broadcast, or a specific contact's direct-message thread) is currently
/// visible on screen. Used to suppress local notifications for messages the user can already see.
/// </summary>
public class AppForegroundState
{
    private readonly object _lock = new();

    /// <summary>True while the app is active/foregrounded (not backgrounded or the screen locked).</summary>
    public bool IsForeground { get; private set; }

    /// <summary>The node number of the direct-message thread currently on screen, or null if none/Everyone.</summary>
    public uint? VisibleContactNodeNum { get; private set; }

    /// <summary>True if the "Everyone" broadcast thread is currently on screen.</summary>
    public bool IsEveryoneThreadVisible { get; private set; }

    public void SetForeground(bool isForeground)
    {
        lock (_lock)
        {
            IsForeground = isForeground;
        }
    }

    /// <summary>Call when a chat thread (Everyone or a specific contact) becomes visible.</summary>
    public void SetVisibleThread(bool isEveryone, uint? contactNodeNum)
    {
        lock (_lock)
        {
            IsEveryoneThreadVisible = isEveryone;
            VisibleContactNodeNum = isEveryone ? null : contactNodeNum;
        }
    }

    /// <summary>Call when the currently visible chat thread is no longer on screen.</summary>
    public void ClearVisibleThread()
    {
        lock (_lock)
        {
            IsEveryoneThreadVisible = false;
            VisibleContactNodeNum = null;
        }
    }

    /// <summary>True if the app is foregrounded and the given thread is the one currently visible.</summary>
    public bool IsThreadVisible(bool isEveryone, uint contactNodeNum)
    {
        lock (_lock)
        {
            if (!IsForeground)
            {
                return false;
            }

            return isEveryone ? IsEveryoneThreadVisible : VisibleContactNodeNum == contactNodeNum;
        }
    }
}
