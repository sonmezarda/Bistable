namespace Bistable.App.ViewModels;

public sealed class SourceDocumentViewModel : ViewModelBase
{
    private string _text;
    private bool _isDirty;

    public SourceDocumentViewModel(string filePath, string relativePath, string text)
    {
        FilePath = filePath;
        RelativePath = relativePath;
        _text = text;
    }

    public string FilePath { get; }
    public string RelativePath { get; }
    public string DisplayName => Path.GetFileName(FilePath);

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value)) IsDirty = true;
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value)) OnPropertyChanged(nameof(TabTitle));
        }
    }

    public string TabTitle => IsDirty ? DisplayName + " •" : DisplayName;

    public void ReplaceFromDisk(string text)
    {
        if (!string.Equals(_text, text, StringComparison.Ordinal))
        {
            _text = text;
            OnPropertyChanged(nameof(Text));
        }
        IsDirty = false;
    }

    public void MarkSaved() => IsDirty = false;
}
