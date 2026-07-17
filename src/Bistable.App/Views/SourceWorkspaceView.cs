using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Bistable.App.ViewModels;
using Engine = Bistable.Engine;

namespace Bistable.App.Views;

/// <summary>
/// IDE-style HDL document: project source list, editable code surface, live
/// reload controls, and clickable elaboration diagnostics.
/// </summary>
public sealed class SourceWorkspaceView : UserControl
{
    private static readonly IBrush Surface = SolidColorBrush.Parse("#151922");
    private static readonly IBrush SurfaceAlt = SolidColorBrush.Parse("#1b202b");
    private static readonly IBrush Stroke = SolidColorBrush.Parse("#2a3241");
    private static readonly IBrush Text = SolidColorBrush.Parse("#d7dde8");
    private static readonly IBrush Muted = SolidColorBrush.Parse("#8f9aad");
    private static readonly IBrush Accent = SolidColorBrush.Parse("#57c7ff");

    private readonly TextEditor _editor;
    private readonly TextBox _searchBox;
    private SourceDocumentViewModel? _observedDocument;
    private bool _syncingEditor;

    public SourceWorkspaceView()
    {
        _editor = new TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = new FontFamily("monospace"),
            FontSize = 13,
            Background = Surface,
            Foreground = Text,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            WordWrap = false
        };
        _editor.TextChanged += OnEditorTextChanged;
        _editor.KeyDown += OnEditorKeyDown;
        _searchBox = new TextBox
        {
            Width = 220,
            Watermark = "Find in file…",
            Background = SurfaceAlt,
            Foreground = Text,
            BorderBrush = Stroke
        };
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                FindNext();
                e.Handled = true;
            }
        };
        Content = BuildLayout();
        DataContextChanged += OnDataContextChanged;
    }

    private Control BuildLayout()
    {
        Grid root = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(6)),
                new RowDefinition(new GridLength(180)) { MinHeight = 90 }
            },
            Background = Surface
        };

        DockPanel toolbar = new()
        {
            LastChildFill = true,
            Background = SurfaceAlt,
            Margin = new Thickness(0),
            Children =
            {
                ToolbarButton("Save  Ctrl+S", "SaveSourceCommand"),
                new CheckBox
                {
                    Content = "Live reload",
                    Foreground = Text,
                    Margin = new Thickness(12, 7, 8, 7),
                    [!ToggleButton.IsCheckedProperty] = new Binding("LiveReloadEnabled", BindingMode.TwoWay)
                },
                new NumericUpDown
                {
                    Width = 90,
                    Minimum = 100,
                    Maximum = 5000,
                    Increment = 50,
                    FormatString = "0 ms",
                    Margin = new Thickness(0, 4, 12, 4),
                    Background = Surface,
                    Foreground = Text,
                    [!NumericUpDown.ValueProperty] = new Binding("LiveReloadDebounceMs", BindingMode.TwoWay)
                },
                _searchBox,
                ToolbarButton("Find next", FindNext),
                new TextBlock
                {
                    Foreground = Muted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(14, 0),
                    [!TextBlock.TextProperty] = new Binding("LiveReloadStatus")
                }
            }
        };
        root.Children.Add(toolbar);

        Grid editorArea = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(230)) { MinWidth = 150 },
                new ColumnDefinition(new GridLength(5)),
                new ColumnDefinition(GridLength.Star)
            }
        };
        ListBox files = new()
        {
            Background = SurfaceAlt,
            BorderBrush = Stroke,
            Foreground = Text,
            ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            [!ItemsControl.ItemsSourceProperty] = new Binding("SourceDocuments"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedSourceDocument", BindingMode.TwoWay),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SourceDocumentViewModel>((document, _) =>
                new StackPanel
                {
                    Margin = new Thickness(7, 5),
                    Children =
                    {
                        new TextBlock
                        {
                            Foreground = Text,
                            FontFamily = new FontFamily("monospace"),
                            FontSize = 12,
                            [!TextBlock.TextProperty] = new Binding(nameof(SourceDocumentViewModel.TabTitle))
                        },
                        new TextBlock
                        {
                            Foreground = Muted,
                            FontSize = 10,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            [!TextBlock.TextProperty] = new Binding(nameof(SourceDocumentViewModel.RelativePath))
                        }
                    }
                }, supportsRecycling: true)
        };
        editorArea.Children.Add(files);
        GridSplitter verticalSplitter = new()
        {
            Width = 5,
            Background = Stroke,
            ResizeDirection = GridResizeDirection.Columns,
            [Grid.ColumnProperty] = 1
        };
        editorArea.Children.Add(verticalSplitter);
        Border editorBorder = new()
        {
            BorderBrush = Stroke,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = _editor,
            [Grid.ColumnProperty] = 2
        };
        editorArea.Children.Add(editorBorder);
        Grid.SetRow(editorArea, 1);
        root.Children.Add(editorArea);

        GridSplitter diagnosticsSplitter = new()
        {
            Height = 6,
            Background = Stroke,
            ResizeDirection = GridResizeDirection.Rows,
            [Grid.RowProperty] = 2
        };
        root.Children.Add(diagnosticsSplitter);

        Grid diagnostics = new()
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            Background = SurfaceAlt,
            [Grid.RowProperty] = 3
        };
        diagnostics.Children.Add(new TextBlock
        {
            Text = "PROBLEMS",
            Foreground = Accent,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(10, 7, 10, 5)
        });
        ListBox problemList = new()
        {
            Background = SurfaceAlt,
            BorderBrush = Brushes.Transparent,
            Foreground = Text,
            [!ItemsControl.ItemsSourceProperty] = new Binding("ElaborationDiagnostics"),
            [!SelectingItemsControl.SelectedItemProperty] = new Binding("SelectedElaborationDiagnostic", BindingMode.TwoWay),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Engine.ElaborationDiagnostic>((diagnostic, _) =>
                new TextBlock
                {
                    Text = diagnostic?.DisplayText ?? string.Empty,
                    Foreground = diagnostic?.Severity == Engine.ElaborationDiagnosticSeverity.Error
                        ? new SolidColorBrush(Color.FromRgb(255, 125, 110))
                        : new SolidColorBrush(Color.FromRgb(255, 196, 100)),
                    FontFamily = new FontFamily("monospace"),
                    FontSize = 11,
                    Margin = new Thickness(8, 4),
                    TextWrapping = TextWrapping.Wrap
                }, supportsRecycling: true),
            [Grid.RowProperty] = 1
        };
        diagnostics.Children.Add(problemList);
        root.Children.Add(diagnostics);
        return root;
    }

    private static Button ToolbarButton(string text, string commandPath) => new()
    {
        Content = text,
        MinHeight = 30,
        Margin = new Thickness(5, 4),
        Background = Surface,
        Foreground = Text,
        BorderBrush = Stroke,
        [!Button.CommandProperty] = new Binding(commandPath)
    };

    private static Button ToolbarButton(string text, Action action)
    {
        Button button = new()
        {
            Content = text,
            MinHeight = 30,
            Margin = new Thickness(5, 4),
            Background = Surface,
            Foreground = Text,
            BorderBrush = Stroke
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (e is AvaloniaPropertyChangedEventArgs args && args.OldValue is MainWindowViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            if (_observedDocument is not null)
            {
                _observedDocument.PropertyChanged -= OnDocumentPropertyChanged;
                _observedDocument = null;
            }
        }
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            SyncSelectedDocument(vm);
            Navigate(vm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel vm) return;
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedSourceDocument)) SyncSelectedDocument(vm);
        else if (e.PropertyName == nameof(MainWindowViewModel.SourceNavigationVersion)) Navigate(vm);
    }

    private void SyncSelectedDocument(MainWindowViewModel vm)
    {
        if (!ReferenceEquals(_observedDocument, vm.SelectedSourceDocument))
        {
            if (_observedDocument is not null) _observedDocument.PropertyChanged -= OnDocumentPropertyChanged;
            _observedDocument = vm.SelectedSourceDocument;
            if (_observedDocument is not null) _observedDocument.PropertyChanged += OnDocumentPropertyChanged;
        }
        _syncingEditor = true;
        try
        {
            _editor.Text = vm.SelectedSourceDocument?.Text ?? string.Empty;
            _editor.IsReadOnly = vm.SelectedSourceDocument is null;
        }
        finally
        {
            _syncingEditor = false;
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceDocumentViewModel.Text)
            && sender is SourceDocumentViewModel document
            && !string.Equals(_editor.Text, document.Text, StringComparison.Ordinal))
        {
            _syncingEditor = true;
            try { _editor.Text = document.Text; }
            finally { _syncingEditor = false; }
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_syncingEditor || DataContext is not MainWindowViewModel vm || vm.SelectedSourceDocument is null) return;
        vm.SelectedSourceDocument.Text = _editor.Text;
    }

    private void Navigate(MainWindowViewModel vm)
    {
        if (vm.SelectedSourceDocument is null) return;
        SyncSelectedDocument(vm);
        int line = Math.Clamp(vm.SourceNavigationLine, 1, Math.Max(1, _editor.Document.LineCount));
        int maxColumn = Math.Max(1, _editor.Document.GetLineByNumber(line).Length + 1);
        _editor.TextArea.Caret.Line = line;
        _editor.TextArea.Caret.Column = Math.Clamp(vm.SourceNavigationColumn, 1, maxColumn);
        _editor.ScrollToLine(line);
        _editor.Focus();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S)
        {
            if (DataContext is MainWindowViewModel vm && vm.SaveSourceCommand.CanExecute(null))
            {
                vm.SaveSourceCommand.Execute(null);
            }
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            _searchBox.Focus();
            e.Handled = true;
        }
    }

    private void FindNext()
    {
        string query = _searchBox.Text ?? string.Empty;
        if (query.Length == 0 || _editor.Text.Length == 0) return;
        int start = Math.Min(_editor.SelectionStart + _editor.SelectionLength, _editor.Text.Length);
        int index = _editor.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = _editor.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return;
        _editor.Select(index, query.Length);
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(index).LineNumber);
        _editor.Focus();
    }
}
