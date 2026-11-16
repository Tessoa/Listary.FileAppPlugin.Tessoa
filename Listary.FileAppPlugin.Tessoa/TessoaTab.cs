using System.Globalization;
using System.Threading.Tasks;

namespace Listary.FileAppPlugin.Tessoa
{
    /// <summary>
    /// A file tab of a tessoa window.
    ///
    /// <para>
    ///   Either "the current tab", resolved by tessoa each time it is asked (from
    ///   <see cref="TessoaWindow.GetCurrentTab"/>), or one exact tab named by tessoa's tab id
    ///   (from <see cref="TessoaWindow.GetTabs"/>). The current tab is not pinned to an id when
    ///   Listary binds the window: the user may switch tabs while typing in the search bar, and
    ///   the jump belongs to the tab in front of them at that moment.
    /// </para>
    /// <para>
    ///   In Columns view a tessoa tab shows several folders side by side, and the answer is the
    ///   folder of the active column, not the leftmost one. That distinction lives in tessoa;
    ///   this class only asks.
    /// </para>
    /// </summary>
    public class TessoaTab : IFileTab, IGetFolder, IOpenFolder, IOpenFolderAndSelectFile
    {
        private readonly TessoaWindow _window;
        private readonly ulong? _uid;

        internal TessoaTab(TessoaWindow window, ulong? uid)
        {
            _window = window;
            _uid = uid;
        }

        /// <returns>
        /// The folder shown in this tab, or an empty string when there is none to report: the start
        /// page, This PC, a preview or terminal tab, the inside of an archive, or a closed tab.
        /// </returns>
        public async Task<string> GetCurrentFolder()
        {
            string folder = _uid is ulong uid
                ? await _window.FolderOf(uid)
                : await TessoaQuery.GetCurrentFolder(_window.ProcessId, _window.Handle);
            PluginLog.Trace(_window.Host?.Logger, "GetCurrentFolder({Handle}, {Tab}) -> '{Folder}'",
                            _window.Handle, Name, folder);
            return folder;
        }

        /// <summary>
        /// Move this tab to <paramref name="path"/>. tessoa navigates the tab in place, so Back
        /// returns to where it was. A tab that cannot show a folder (a preview, for example), or
        /// one closed in the meantime, gets a new tab in the same window instead.
        /// </summary>
        public Task<bool> OpenFolder(string path) => _window.Open(path, select: false, tab: Name);

        public Task<bool> OpenFolderAndSelectFile(string path) => _window.Open(path, select: true, tab: Name);

        private string Name => _uid is ulong uid ? uid.ToString(CultureInfo.InvariantCulture) : "current";
    }
}
