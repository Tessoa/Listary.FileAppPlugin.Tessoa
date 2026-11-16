using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Listary.FileAppPlugin.Tessoa
{
    /// <summary>
    /// One tessoa window.
    ///
    /// <para>
    ///   A tessoa window holds a split tree of panes, each pane a stack of tabs. tessoa reports
    ///   every file tab of the window over the query pipe (<see cref="TessoaQuery.GetTabs"/>),
    ///   the current one first, so Quick Switch and the opened folders menu see all of them.
    /// </para>
    /// <para>
    ///   Opening at window level adds a tab to this window, or switches to the tab that already
    ///   shows the folder. Opening at tab level moves that tab in place. Both need file
    ///   application API 2 in the running tessoa; against an older one they fall back to the
    ///   application-level behaviour, which lets tessoa pick the tab.
    /// </para>
    /// </summary>
    public class TessoaWindow : IFileWindow, IGetFileTabs, IOpenFolder, IOpenFolderAndSelectFile
    {
        /// <summary>
        /// How long one tab listing answers <see cref="TessoaTab.GetCurrentFolder"/> for the
        /// tabs it produced. Listary asks every tab right after listing them; one pipe round trip
        /// serves the whole batch instead of one per tab.
        /// </summary>
        private const int SnapshotMs = 1000;

        private readonly IFileAppPluginHost _host;
        private readonly TessoaLauncher _launcher;
        private readonly object _gate = new object();
        private readonly Stopwatch _snapshotAge = new Stopwatch();
        private TabList _snapshot;

        public IntPtr Handle { get; }

        internal int ProcessId { get; }

        internal IFileAppPluginHost Host => _host;

        internal TessoaWindow(IFileAppPluginHost host, IntPtr hWnd, int processId, TessoaLauncher launcher)
        {
            _host = host;
            Handle = hWnd;
            ProcessId = processId;
            _launcher = launcher;
        }

        public Task<IFileTab> GetCurrentTab()
        {
            PluginLog.Trace(_host?.Logger, "GetCurrentTab({Handle})", Handle);
            return Task.FromResult<IFileTab>(new TessoaTab(this, null));
        }

        public async Task<IEnumerable<IFileTab>> GetTabs()
        {
            TabList list = await Refresh();
            PluginLog.Trace(_host?.Logger, "GetTabs({Handle}) -> {Count}", Handle,
                            list == null ? "unsupported" : list.Tabs.Count.ToString(CultureInfo.InvariantCulture));
            // An older tessoa cannot list tabs. Report its current tab, as before.
            if (list == null)
            {
                return new IFileTab[] { new TessoaTab(this, null) };
            }
            return list.Tabs.Select(t => (IFileTab)new TessoaTab(this, t.Uid)).ToList();
        }

        public Task<bool> OpenFolder(string path) => Open(path, select: false, tab: null);

        public Task<bool> OpenFolderAndSelectFile(string path) => Open(path, select: true, tab: null);

        /// <summary>
        /// Open at window level when <paramref name="tab"/> is <c>null</c>, otherwise at tab level.
        /// <paramref name="tab"/> is <c>"current"</c> or a tab id.
        /// </summary>
        internal async Task<bool> Open(string path, bool select, string tab)
        {
            string handle = Handle.ToInt64().ToString(CultureInfo.InvariantCulture);
            string scope = tab == null ? "--in-window=" + handle : "--in-tab=" + handle + ":" + tab;
            if (await Api() < 2)
            {
                scope = null;
            }
            PluginLog.Trace(_host?.Logger, "Open({Handle}, select: {Select}, {Scope})", Handle, select,
                            scope ?? "application level");
            return await _launcher.Open(path, select, scope);
        }

        /// <summary>
        /// The folder of tab <paramref name="uid"/>, from the latest listing when it is recent.
        /// An empty string when the tab is gone.
        /// </summary>
        internal async Task<string> FolderOf(ulong uid)
        {
            TabList list = Recent() ?? await Refresh();
            return list?.Tabs.FirstOrDefault(t => t.Uid == uid)?.Folder ?? string.Empty;
        }

        private async Task<int> Api()
        {
            TabList list = Recent() ?? await Refresh();
            return list?.Api ?? 1;
        }

        private TabList Recent()
        {
            lock (_gate)
            {
                return _snapshot != null && _snapshotAge.ElapsedMilliseconds < SnapshotMs ? _snapshot : null;
            }
        }

        private async Task<TabList> Refresh()
        {
            TabList list = await TessoaQuery.GetTabs(ProcessId, Handle);
            lock (_gate)
            {
                _snapshot = list;
                _snapshotAge.Restart();
            }
            return list;
        }
    }
}
