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
        private readonly int _processId;

        public IntPtr Handle { get; }

        public TessoaWindow(IntPtr hWnd, int processId)
        {
            Handle = hWnd;
            _processId = processId;
        }

        public Task<IFileTab> GetCurrentTab()
        {
            return Task.FromResult<IFileTab>(new TessoaTab(Handle, _processId));
        }
    }
}
