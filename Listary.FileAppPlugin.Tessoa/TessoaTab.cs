using System;
using System.Threading.Tasks;

namespace Listary.FileAppPlugin.Tessoa
{
    /// <summary>
    /// The active tab of a tessoa window.
    ///
    /// <para>
    ///   In Columns view a tessoa tab shows several folders side by side, and the answer is the
    ///   folder of the active column — not the leftmost one. That distinction lives in tessoa;
    ///   this class only asks.
    /// </para>
    /// </summary>
    public class TessoaTab : IFileTab, IGetFolder
    {
        private readonly IFileAppPluginHost _host;
        private readonly IntPtr _hWnd;
        private readonly int _processId;

        public TessoaTab(IFileAppPluginHost host, IntPtr hWnd, int processId)
        {
            _host = host;
            _hWnd = hWnd;
            _processId = processId;
        }

        /// <returns>
        /// The folder shown in this tab, or an empty string when there is none to report: the start
        /// page, This PC, a preview or terminal tab, or the inside of an archive.
        /// </returns>
        public async Task<string> GetCurrentFolder()
        {
            string folder = await TessoaQuery.GetCurrentFolder(_processId, _hWnd);
            PluginLog.Trace(_host?.Logger, "GetCurrentFolder({Handle}) -> '{Folder}'", _hWnd, folder);
            return folder;
        }
    }
}
