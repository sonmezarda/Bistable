namespace Bistable.App.ViewModels;

public sealed class HierarchyBreadcrumbItemViewModel
{
    public HierarchyBreadcrumbItemViewModel(string hierarchyPath, string title, string moduleName, bool isCurrent)
    {
        HierarchyPath = hierarchyPath;
        Title = title;
        ModuleName = moduleName;
        IsCurrent = isCurrent;
    }

    public string HierarchyPath { get; }

    public string Title { get; }

    public string ModuleName { get; }

    public bool IsCurrent { get; }

    public string DisplayLabel => IsCurrent ? Title : $"{Title}";
}
