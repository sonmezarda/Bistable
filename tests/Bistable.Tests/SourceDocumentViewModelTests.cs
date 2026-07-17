using Bistable.App.ViewModels;

namespace Bistable.Tests;

public sealed class SourceDocumentViewModelTests
{
    [Fact]
    public void TextEdit_MarksDirty_AndDiskReplacementClearsDirty()
    {
        SourceDocumentViewModel document = new("/tmp/top.sv", "top.sv", "assign y = a;");

        document.Text = "assign y = ~a;";

        Assert.True(document.IsDirty);
        Assert.EndsWith(" •", document.TabTitle);

        document.ReplaceFromDisk("assign y = a & b;");

        Assert.False(document.IsDirty);
        Assert.Equal("assign y = a & b;", document.Text);
    }
}
