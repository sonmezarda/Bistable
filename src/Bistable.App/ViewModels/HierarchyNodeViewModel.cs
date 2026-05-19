using System.Collections.ObjectModel;
using Bistable.Core.Design;

namespace Bistable.App.ViewModels;

public sealed class HierarchyNodeViewModel : ViewModelBase
{
    public HierarchyNodeViewModel(DesignHierarchyNode node)
    {
        InstanceName = node.InstanceName;
        ModuleName = node.ModuleName;
        HierarchyPath = node.HierarchyPath;
        foreach (DesignHierarchyNode child in node.Children)
        {
            Children.Add(new HierarchyNodeViewModel(child));
        }
    }

    public string InstanceName { get; }

    public string ModuleName { get; }

    public string HierarchyPath { get; }

    public string DisplayLabel => $"{InstanceName} : {ModuleName}";

    public ObservableCollection<HierarchyNodeViewModel> Children { get; } = [];
}
