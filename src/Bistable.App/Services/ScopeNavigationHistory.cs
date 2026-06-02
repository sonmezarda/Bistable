namespace Bistable.App.Services;

// P2.7-2: scope back/forward history. Browser-style stack semantics:
//   - Recording a new navigation pushes the previous path onto the past stack
//     and clears the future stack.
//   - Back pops the past and pushes the current onto the future.
//   - Forward does the reverse.
//   - Same-path or null navigations are ignored (no stack pollution).
//
// Lifted out of MainWindowViewModel so the back/forward logic can be tested in
// isolation and the VM gets a clean injection point.
public sealed class ScopeNavigationHistory
{
    private readonly List<string> _past = [];
    private readonly Stack<string> _future = new();
    private string? _current;

    public bool CanGoBack => _past.Count > 0;
    public bool CanGoForward => _future.Count > 0;
    public string? Current => _current;

    /// <summary>
    /// Record a forward navigation to <paramref name="newPath"/>. The previous
    /// path (if any) is pushed onto the past stack and the future is cleared.
    /// No-op when the new path equals the current path (case-insensitive).
    /// </summary>
    public void RecordNavigation(string? newPath)
    {
        if (string.Equals(_current, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (_current is { } prev)
        {
            _past.Add(prev);
            _future.Clear();
        }
        _current = newPath;
    }

    /// <summary>
    /// Pop the past stack. Returns the path to navigate to, or null when there
    /// is no past to go back to. The previous current is pushed onto the future.
    /// </summary>
    public string? GoBack()
    {
        if (_past.Count == 0) return null;
        string previous = _past[^1];
        _past.RemoveAt(_past.Count - 1);
        if (_current is { } cur) _future.Push(cur);
        _current = previous;
        return previous;
    }

    /// <summary>
    /// Pop the future stack. Returns the path to navigate to, or null when
    /// there is nothing to go forward to.
    /// </summary>
    public string? GoForward()
    {
        if (_future.Count == 0) return null;
        string next = _future.Pop();
        if (_current is { } cur) _past.Add(cur);
        _current = next;
        return next;
    }
}
