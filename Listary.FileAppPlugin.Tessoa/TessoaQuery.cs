using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace Listary.FileAppPlugin.Tessoa
{
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
            return Task.Run(() => Query(processId, hWnd));
        }

        private static string Query(int processId, IntPtr hWnd)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipePrefix + processId,
                                                            PipeDirection.InOut))
                {
                    pipe.Connect(ConnectTimeoutMs);

                    byte[] body = Encoding.UTF8.GetBytes(
                        "v " + ProtocolVersion + "\n" +
                        "op query-cwd\n" +
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
