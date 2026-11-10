using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// A snapshot with nothing left, a draft with nothing written, and an editor opened on a macro.
public partial class RecoveryTests
{
    [Fact]
    public void A_snapshot_with_nothing_left_in_it_is_removed()
    {
        InOwnFile(path =>
        {
            RecoveryStore.SaveProject([Named("login")], [], null);
            Assert.True(File.Exists(path));

            // Saving the project, or letting the work go, takes the last thing out of the file.
            RecoveryStore.ClearProject();

            Assert.False(File.Exists(path));
            Assert.Null(RecoveryStore.Load());
        });
    }

    /// <summary>
    /// 一张什么都没写的草稿不该留下：新建一个宏就关掉程序，下次启动不该问"发现上次未保存的
    /// 内容"，因为答"恢复"回来的还是同一张空表单。名字还是新宏的默认名、没有步骤、没有描述、
    /// 没有绑定键，就是这种草稿。
    /// </summary>
    [Fact]
    public void A_draft_with_nothing_written_in_it_is_not_kept()
    {
        InOwnFile(path =>
        {
            var blank = new MacroItem { Name = MacroNames.NextDefault([]) };

            RecoveryStore.SaveEditor(blank, string.Empty);

            Assert.False(File.Exists(path), "a blank draft was written to the snapshot");
            Assert.Null(RecoveryStore.Load());
        });
    }

    [Fact]
    public void An_editor_opened_on_a_macro_comes_up_as_saved()
    {
        Ui.Run(() =>
        {
            var macro = Named("login");
            var window = new MacroEditorWindow(macro, [macro], null);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = (MacroEditorViewModel)window.DataContext!;
            Assert.False(editor.IsDirty, "the editor came up unsaved");
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>
    /// 新建一个宏、什么都还没写，就不算"有未保存的东西"。新建时编辑器是空的，一步也没有；
    /// 若它一开起来就算改过，快照里就会留下一张空草稿，下次启动便凭空问一句"发现上次未保存的
    /// 内容"，而答了也是同一张空表单。
    /// </summary>
    [Fact]
    public void An_editor_opened_on_a_new_macro_comes_up_as_saved()
    {
        Ui.Run(() =>
        {
            var window = new MacroEditorWindow(null, [], null);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = (MacroEditorViewModel)window.DataContext!;

            Assert.False(editor.IsDirty, "a new macro came up unsaved");
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }
}
