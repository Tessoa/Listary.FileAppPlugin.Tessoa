using System;
using System.Threading.Tasks;

namespace Listary.FileAppPlugin.Tessoa
{
    /// <summary>
    /// Listary file application plugin for tessoa.
    ///
    /// <para>
    ///   tessoa is a file manager, so it acts as a source of opened folders (Quick Switch reads
    ///   from it) rather than a Quick Switch target.
    /// </para>
    /// </summary>
    public class TessoaPlugin : IFileAppPlugin, IOpenFolder, IOpenFolderAndSelectFile
    {
        private const string ExecutableName = "\\tessoa.exe";

        private IFileAppPluginHost _host;
        private TessoaLauncher _launcher;

        public bool IsOpenedFolderProvider => true;

        public bool IsQuickSwitchTarget => false;

        public bool IsSharedAcrossApplications => false;

        public SearchBarType SearchBarType => SearchBarType.Floating;

        public Task<bool> Initialize(IFileAppPluginHost host)
        {
            _host = host;
            _launcher = new TessoaLauncher(host.Logger);
            return Task.FromResult(true);
        }

        // Where the folder lands is tessoa's call, not ours: it reuses an open tab when there
        // is one and opens a new tab otherwise. Targeting one exact window or tab would need
        // the window- and tab-level interfaces, and a forwarding protocol that carries a target.
        public Task<bool> OpenFolder(string path) => _launcher.Open(path, select: false);

        public Task<bool> OpenFolderAndSelectFile(string path) => _launcher.Open(path, select: true);

        public IFileWindow BindFileWindow(IntPtr hWnd)
        {
            if (!Win32Utils.IsTopLevelWindow(hWnd))
            {
                return null;
            }

            int processId = Win32Utils.GetProcessId(hWnd);
            if (processId == 0)
            {
                return null;
            }

            string path = Win32Utils.GetProcessPath(processId);
            if (!path.EndsWith(ExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            _launcher.RememberExecutable(path);

            PluginLog.Trace(_host?.Logger, "BindFileWindow({Handle}) -> pid {ProcessId}",
                            hWnd, processId);
            return new TessoaWindow(_host, hWnd, processId);
        }
    }
}
