using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
    public class TessoaPlugin : IFileAppPlugin
    {
        private const string ExecutableName = "\\tessoa.exe";

        private IFileAppPluginHost _host;

        public bool IsOpenedFolderProvider => true;

        public bool IsQuickSwitchTarget => false;

        public bool IsSharedAcrossApplications => false;

        public SearchBarType SearchBarType => SearchBarType.Floating;

        public Task<bool> Initialize(IFileAppPluginHost host)
        {
            _host = host;
            return Task.FromResult(true);
        }

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

            _host?.Logger?.LogDebug("Bound tessoa window {Handle} of process {ProcessId}",
                                    hWnd, processId);
            return new TessoaWindow(hWnd, processId);
        }
    }
}
