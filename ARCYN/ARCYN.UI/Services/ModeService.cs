using ARCYN.UI.Models;

namespace ARCYN.UI.Services;

public sealed class ModeService
{
    private readonly List<ModeConfig> _modes;
    private readonly AppState _state;

    private bool HasSelection => _state.SelectedModeIndex >= 0 && _state.SelectedModeIndex < _modes.Count;

    public string ActiveLabel
    {
        get
        {
            if (!HasSelection) return "STANDBY";
            return _modes[_state.SelectedModeIndex].Name.ToUpperInvariant();
        }
    }

    public int TotalProcesses => _modes.Sum(m => m.ProcessCount);

    public ModeService(AppState state, List<ModeConfig>? modes = null)
    {
        _state = state;
        _modes = modes ?? [];
    }

    public void Add(ModeConfig mode)
    {
        _modes.Add(mode);
        Reindex();
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _modes.Count) return false;
        _modes.RemoveAt(index);
        Reindex();
        return true;
    }

    public bool MoveUp(int index)
    {
        if (index <= 0 || index >= _modes.Count) return false;
        (_modes[index], _modes[index - 1]) = (_modes[index - 1], _modes[index]);
        Reindex();
        return true;
    }

    public void Insert(int index, ModeConfig mode)
    {
        if (index < 0) index = 0;
        if (index > _modes.Count) index = _modes.Count;
        _modes.Insert(index, mode);
        Reindex();
    }

    public bool MoveDown(int index)
    {
        if (index < 0 || index >= _modes.Count - 1) return false;
        (_modes[index], _modes[index + 1]) = (_modes[index + 1], _modes[index]);
        Reindex();
        return true;
    }

    private void Reindex()
    {
        for (int i = 0; i < _modes.Count; i++)
            _modes[i].Index = i + 1;
    }
}
