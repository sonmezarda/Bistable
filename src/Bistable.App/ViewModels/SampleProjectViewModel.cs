using System.Windows.Input;

namespace Bistable.App.ViewModels;

public sealed class SampleProjectViewModel(string name, string path, ICommand openCommand)
{
    public string Name { get; } = name;

    public string Path { get; } = path;

    public ICommand OpenCommand { get; } = openCommand;
}
