using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace Listary.FileAppPlugin.Tessoa
{
    /// <summary>One file tab as tessoa reports it.</summary>
    internal sealed class TabInfo
    {
        /// <summary>tessoa's id for the tab. Stable while the tab lives, unique in the process.</summary>
        public ulong Uid { get; }

        /// <summary>Whether this is the window's current tab.</summary>
        public bool IsCurrent { get; }

        public string Folder { get; }

        public TabInfo(ulong uid, bool isCurrent, string folder)
        {
            Uid = uid;
            IsCurrent = isCurrent;
            Folder = folder;
        }
    }

    /// <summary>The answer to <c>query-tabs</c>.</summary>
    internal sealed class TabList
    {
        /// <summary>File application API level of the running process.</summary>
        public int Api { get; }

        public IReadOnlyList<TabInfo> Tabs { get; }

        public TabList(int api, IReadOnlyList<TabInfo> tabs)
        {
            Api = api;
            Tabs = tabs;
        }
    }

    /// <summary>
    /// Client for tessoa's current-path query pipe.
    ///
    /// <para>
    ///   tessoa draws its own UI. It has no UI Automation tree and no standard controls, so the
    ///   current folder cannot be read out of the window. Instead every tessoa process serves a
    ///   read-only named pipe:
    /// </para>
    /// <code>\\.\pipe\tessoa.q.&lt;pid&gt;</code>
    /// <para>
    ///   The pipe is keyed by process id because that is the one thing a plugin can always derive
    ///   from a window handle (<c>GetWindowThreadProcessId</c>). It also keeps installed and
    ///   portable copies of tessoa apart when both are running.
    /// </para>
    ///
    /// <para>Request and response frames are both <c>4-byte little-endian length + UTF-8 body</c>.</para>
    /// <code>
    /// v 1
    /// op query-cwd
    /// hwnd 123456
    /// </code>
    /// <para>
    ///   A zero-length response is a normal answer meaning "no folder to report" — the tab is on
    ///   the start page, on This PC, inside an archive, or the window handle is unknown. tessoa
    ///   deliberately never falls back to some previous folder in that case, and neither does this
    ///   client: <see cref="GetCurrentFolder"/> returns an empty string.
    /// </para>
    /// </summary>
    internal static class TessoaQuery
    {
        private const string PipePrefix = "tessoa.q.";
        private const int ProtocolVersion = 1;

        /// <summary>Milliseconds to wait for the pipe. A query is a memory read on the other side.</summary>
        private const int ConnectTimeoutMs = 300;

        /// <summary>Upper bound on the response body, mirroring the server's own cap.</summary>
        private const int MaxResponseBytes = 1 << 20;

        /// <summary>
        /// Ask the tessoa process <paramref name="processId"/> which folder the window
        /// <paramref name="hWnd"/> is showing.
        /// </summary>
        /// <returns>The folder path, or an empty string if there is nothing to report.</returns>
        public static Task<string> GetCurrentFolder(int processId, IntPtr hWnd)
        {
            // The pipe API used here is synchronous, so the call is moved off the caller's thread
            // rather than blocking it.
            return Task.Run(() => Query(processId, hWnd, "query-cwd"));
        }

        /// <summary>
        /// Ask which folders the window <paramref name="hWnd"/> has open: one entry per file tab,
        /// the current tab first.
        /// </summary>
        /// <returns>
        /// <c>null</c> when the process does not understand the request. tessoa before file
        /// application API 2 answers any request it does not know with an empty frame, while a
        /// newer tessoa always starts its answer with an <c>api</c> line, even with no tabs.
        /// </returns>
        /// <remarks>
        /// <code>
        /// api 2
        /// tab 17 c D:\Work
        /// tab 23 b C:\Users\me\Downloads
        /// </code>
        /// The letter is <c>c</c> for the current tab, <c>v</c> for the front tab of another tab
        /// group and <c>b</c> for a background tab. The path is the rest of the line.
        /// </remarks>
        public static Task<TabList> GetTabs(int processId, IntPtr hWnd)
        {
            return Task.Run(() => ParseTabs(Query(processId, hWnd, "query-tabs")));
        }

        internal static TabList ParseTabs(string answer)
        {
            if (string.IsNullOrEmpty(answer))
            {
                return null;
            }
            int api = 0;
            var tabs = new List<TabInfo>();
            foreach (string line in answer.Split('\n'))
            {
                if (line.StartsWith("api ", StringComparison.Ordinal))
                {
                    int.TryParse(line.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out api);
                    continue;
                }
                if (!line.StartsWith("tab ", StringComparison.Ordinal))
                {
                    continue; // Unknown lines are ignored, the same rule tessoa follows.
                }
                string[] parts = line.Split(new[] { ' ' }, 4);
                if (parts.Length == 4 && parts[2].Length == 1 && parts[3].Length > 0
                    && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong uid))
                {
                    tabs.Add(new TabInfo(uid, parts[2] == "c", parts[3]));
                }
            }
            // An answer without an api line is not one this client understands.
            return api > 0 ? new TabList(api, tabs) : null;
        }

        private static string Query(int processId, IntPtr hWnd, string op)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipePrefix + processId,
                                                            PipeDirection.InOut))
                {
                    pipe.Connect(ConnectTimeoutMs);

                    byte[] body = Encoding.UTF8.GetBytes(
                        "v " + ProtocolVersion + "\n" +
                        "op " + op + "\n" +
                        "hwnd " + hWnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");

                    byte[] request = new byte[4 + body.Length];
                    WriteLength(request, body.Length);
                    Buffer.BlockCopy(body, 0, request, 4, body.Length);
                    pipe.Write(request, 0, request.Length);
                    pipe.Flush();

                    byte[] header = new byte[4];
                    if (!ReadExact(pipe, header, 4))
                    {
                        return string.Empty;
                    }
                    int length = ReadLength(header);
                    if (length <= 0 || length > MaxResponseBytes)
                    {
                        // 0 is the normal "nothing to report" answer.
                        return string.Empty;
                    }

                    byte[] response = new byte[length];
                    if (!ReadExact(pipe, response, length))
                    {
                        return string.Empty;
                    }
                    return Encoding.UTF8.GetString(response);
                }
            }
            catch (TimeoutException)
            {
                // tessoa is not listening: closing down, or an older build without the pipe.
                return string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    return false;
                }
                offset += read;
            }
            return true;
        }

        private static void WriteLength(byte[] buffer, int value)
        {
            buffer[0] = (byte)(value & 0xFF);
            buffer[1] = (byte)((value >> 8) & 0xFF);
            buffer[2] = (byte)((value >> 16) & 0xFF);
            buffer[3] = (byte)((value >> 24) & 0xFF);
        }

        private static int ReadLength(byte[] buffer)
        {
            return buffer[0]
                 | (buffer[1] << 8)
                 | (buffer[2] << 16)
                 | (buffer[3] << 24);
        }
    }
}
