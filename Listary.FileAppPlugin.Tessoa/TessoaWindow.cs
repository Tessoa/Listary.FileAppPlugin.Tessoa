using System;
using System.Threading.Tasks;

namespace Listary.FileAppPlugin.Tessoa
{
    /// <summary>
    /// One tessoa window.
    ///
    /// <para>
    ///   A tessoa window holds a split tree of panes, each pane a stack of tabs. Which of those is
    ///   current changes constantly, so the plugin does not track them: it forwards the window
    ///   handle and lets tessoa answer for whatever is focused inside that window.
    /// </para>
    /// </summary>
    public class TessoaWindow : IFileWindow
    {
        private readonly IFileAppPluginHost _host;
        private readonly int _processId;

        public IntPtr Handle { get; }

        public TessoaWindow(IFileAppPluginHost host, IntPtr hWnd, int processId)
        {
            _host = host;
            Handle = hWnd;
            _processId = processId;
        }

        public Task<IFileTab> GetCurrentTab()
        {
            PluginLog.Trace(_host?.Logger, "GetCurrentTab({Handle})", Handle);
            return Task.FromResult<IFileTab>(new TessoaTab(_host, Handle, _processId));
        }
    }
}
