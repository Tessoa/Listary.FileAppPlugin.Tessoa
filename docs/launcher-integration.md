# Integrating a launcher with tessoa

[简体中文](launcher-integration.zh-CN.md)

For developers of launchers, search tools and file-dialog helpers (Listary-style software) that
want to work with [tessoa](https://github.com/Tessoa), a GPU-accelerated file manager for
Windows.

tessoa draws its own UI. There is no UI Automation tree and there are no standard controls, so
the usual tricks (reading an address bar, sending keystrokes) do not work. Instead tessoa offers
two stable interfaces:

| Direction | Interface | Use it to |
|---|---|---|
| Read | A read-only named pipe per process | Ask which folder a window shows, or list all its tabs |
| Write | Command-line arguments to `tessoa.exe` | Open a folder, or reveal a file, at application, window or tab level |

Both are Windows only. The reference implementation is the Listary plugin in this repository
(C#, MIT).

---

## 1. Recognize a tessoa window

A tessoa window is a top-level window whose process image is `tessoa.exe`.

```
GetWindowThreadProcessId(hwnd)  ->  pid
QueryFullProcessImageName(pid)  ->  ends with "\tessoa.exe" (case-insensitive)
```

One tessoa process can own several windows. Always keep the window handle (HWND) next to the
process id: every interface below takes the HWND, so answers are about the window the user is
looking at, not just "some tessoa window".

tessoa also runs helper processes with the same image name (for example a shell context-menu
host started with `--shell-menu-host=`). They have no visible top-level window. Start from the
window, not from a process list, and they will not get in the way.

## 2. Read: the query pipe

### Pipe name

```
\\.\pipe\tessoa.q.<pid>
```

The name contains the process id, so you can go straight from a window handle to the right pipe,
even with a portable and an installed copy running side by side. To discover running instances
without a window handle, list `\\.\pipe\` and keep the names starting with `tessoa.q.`.

The pipe is read-only. It accepts only the two query operations below and never makes tessoa do
anything. Asking does not bring a window to the front and leaves no trace in tessoa's logs.

### Framing

Each connection carries one request and one response. Both are framed the same way:

```
[4-byte little-endian length][UTF-8 body]
```

Connect, write one frame, read one frame, close. Use a timeout: 1 second for the whole exchange
is generous, because answers come from a table tessoa keeps ready and does not wait for its UI
thread. If the pipe is busy (`ERROR_PIPE_BUSY`), wait up to about 200 ms and try again.

### Request body

Line-based. Each line is a key, a space and a value. Lines end with `\n`.

```
v 1
op query-cwd
hwnd 123456
```

| Line | Meaning |
|---|---|
| `v 1` | Required. Marks the frame as tessoa's protocol. |
| `op <name>` | `query-cwd` or `query-tabs`. |
| `hwnd <decimal>` | Optional. The window you are asking about. Without it, tessoa answers for its focused window. A value that does not parse as a number is treated as absent, so send the handle as a plain decimal integer. |

Unknown lines are ignored, so later versions can add lines without breaking you.

### `op query-cwd`: the current folder of a window

The response body is one absolute path in UTF-8, with no trailing newline.

**A zero-length response is a normal answer.** It means "this window has no folder to report":
the current tab is a start page, *This PC*, a preview, a terminal or a settings tab, or the
handle is not a tessoa window. Treat it as "nothing here". Do not fall back to another window or
guess. tessoa itself never answers with a different window's folder when you name a window.

During the first second or two after start-up, before the first window exists, the answer is
also empty.

### `op query-tabs`: every folder tab of a window

Requires file application API 2 (see §4). The response looks like this:

```
api 2
tab 17 c D:\Work
tab 23 v D:\Photos\2026
tab 31 b C:\Users\me\Downloads
```

- The first line is always `api <n>`, even when there are no tabs or the window is unknown.
- Then one line per folder tab: `tab <id> <place> <path>`. The path is the rest of the line and
  may contain spaces.
- `<place>`: `c` is the current tab (the front tab of the focused tab group), `v` is the front
  tab of another visible tab group (tessoa can split a window into groups), `b` is a background
  tab.
- The current tab comes first. The rest follow layout order, then tab order within a group.
- Tabs without a folder (preview, terminal and so on) are left out.
- `<id>` is tessoa's id for that tab. It stays the same while the tab navigates, and you pass it
  back to open something in that exact tab (§3). Ids are only valid for the lifetime of the
  process.

An older tessoa answers `query-tabs` with an **empty frame**. That is how you tell the two apart
without starting anything.

### Example (C#)

```csharp
static string Query(int pid, IntPtr hwnd, string op)
{
    using var pipe = new NamedPipeClientStream(".", "tessoa.q." + pid, PipeDirection.InOut);
    pipe.Connect(1000);
    byte[] body = Encoding.UTF8.GetBytes($"v 1\nop {op}\nhwnd {hwnd.ToInt64()}\n");
    pipe.Write(BitConverter.GetBytes(body.Length), 0, 4);
    pipe.Write(body, 0, body.Length);
    pipe.Flush();

    byte[] head = ReadExactly(pipe, 4);
    int length = BitConverter.ToInt32(head, 0);
    return length == 0 ? "" : Encoding.UTF8.GetString(ReadExactly(pipe, length));
}
```

`ReadExactly` loops over `Read` until it has the requested number of bytes. A connection
failure means that process is not serving (not running, or exiting). A connected but empty
answer means "running, nothing to report". Keep the two apart in your diagnostics.

## 3. Write: command-line arguments

To make tessoa go somewhere, start `tessoa.exe` with arguments. If tessoa is already running,
the new process hands the request to the running one and exits within milliseconds. If it is not
running, the new process becomes tessoa itself and opens the target.

Pass each argument as **one** quoted argument, including the flag name, so paths with spaces
survive:

```
tessoa.exe "--open-window=D:\My Projects"
```

### Actions

| Argument | Effect |
|---|---|
| `--open-window=<folder>` | Show that folder. |
| `--reveal-window=<path>` | Show the parent folder, then select the file or folder and scroll it into view. |

The flag names are historical. tessoa does not always open a new window. With no scope (below)
it reuses what is already there:

1. A tab group whose front tab already shows the folder gets the focus.
2. Otherwise a background tab showing it is brought to the front.
3. Otherwise a new tab opens in the focused tab group of the current window.
4. Only when no window exists at all is a window created.

Repeating the same request therefore does not pile up duplicate tabs.

Paths must be absolute and must exist. `--open-window=` needs a folder. Revealing waits for slow
folders to finish loading, and refreshes a tab whose listing is stale, so a file that was created
a moment ago is still found.

### Scope: window and tab level

Requires file application API 2. Add **one** of these as a separate argument:

| Argument | Level | Effect |
|---|---|---|
| `--in-window=<hwnd>` | Window | Same reuse rules, but only inside that window. |
| `--in-tab=<hwnd>` | Tab | Navigate that window's current tab in place. |
| `--in-tab=<hwnd>:<id>` | Tab | Bring the tab with that id (from `query-tabs`) to the front and navigate it in place. |

```
tessoa.exe "--open-window=D:\Work" "--in-tab=123456"
tessoa.exe "--reveal-window=D:\Work\notes.txt" "--in-tab=123456:23"
```

Tab-level navigation behaves like clicking a folder inside tessoa. It goes into the tab's
history, so Back returns to the previous folder. If the target is already shown, nothing moves
and the request still succeeds.

When the target is gone by the time tessoa handles the request, it steps down a level instead of
failing:

| Requested | Falls back to | When |
|---|---|---|
| Tab | Window | The tab no longer exists, or cannot show a folder (preview, terminal and so on). |
| Window | Application | The window no longer exists. |

Window handles change when the user switches graphics adapters in tessoa, so a handle you stored
earlier may have gone stale. The fallback still lands somewhere sensible.

**Ask for "current" at the moment you act.** Use `--in-tab=<hwnd>` rather than remembering the
current tab's id: the user may have switched tabs while typing into your search box.

### Exit codes

| Code | Meaning | What to do |
|---|---|---|
| `0` | Request delivered, or this process became tessoa and opened the target. | Done. |
| `1` | The running tessoa did not accept the request, typically because it is older than the executable you started and does not know the scope. | Retry once without the scope argument. |
| `2` | Invalid arguments: missing or non-existent path, a file given to `--open-window=`, an unknown scope value, a scope without an action, or two scopes. | Do not retry. |

If tessoa was not running, the process you started *is* tessoa and keeps running. Do not wait for
it to exit. Wait a few seconds; if it is still alive, treat that as success. The Listary plugin
waits 3.5 seconds.

## 4. Capability detection

Never start an unknown tessoa just to see whether it understands a flag: an old version ignores
unknown flags and opens a window, so the probe itself changes the user's screen.

| Capability | How to detect |
|---|---|
| Reading the current folder (`query-cwd`) | The pipe exists. Available since tessoa 0.22.0. |
| `--open-window=` / `--reveal-window=` (API 1) | The `Comments` field of the executable's version resource contains exactly `tessoa-file-app-api=1`. Read it from the file without starting it. |
| `query-tabs`, `--in-window=`, `--in-tab=` (API 2) | Send `query-tabs` to the **running** process. A first line of `api 2` or higher means supported; an empty frame means not supported. |

The `Comments` marker stays at `=1` on purpose, because existing integrations compare it for
equality. API 2 is reported by the running process instead, which is also more accurate: if the
user replaced the executable without restarting tessoa, the running process is what counts.

## 5. Finding `tessoa.exe`

Suggested order:

1. A path the user configured explicitly. If it is invalid, report an error rather than quietly
   using another installation.
2. The executable of a tessoa window you have seen (from §1).
3. `App Paths\tessoa.exe` under HKCU, then HKLM, in both registry views.
4. `%LOCALAPPDATA%\tessoa\tessoa.exe`, the default installation.

Portable copies can live anywhere, which is why the user setting comes first.

## 6. Checking your integration without writing code

tessoa answers the same pipe from its own command line:

```
tessoa.exe --print-cwd                      # focused window of any running instance
tessoa.exe --print-cwd=<pid>:<hwnd>         # one specific window
tessoa.exe --print-cwd --print-cwd-out=x.txt
```

Exit code `0` printed a path, `1` means nothing to report or not running, `2` means invalid
arguments. The release build is a GUI program and has no console, so use `--print-cwd-out=` to
capture the answer in scripts.

## 7. Rules worth keeping

- **Empty means empty.** Never substitute another window's folder or a remembered one.
- **Keep the HWND.** Ask about the window the user is looking at, not about the process.
- **Do not cache answers for long.** The folder changes whenever the user navigates. Re-ask when
  your UI opens; a cache of about a second is fine.
- **One scope per request.** Give at most one of `--in-window=` and `--in-tab=`.
- **Retry once on exit code 1, never on 2.**
- **The pipe only reads.** All actions go through the command line, so that they share tessoa's
  single-instance handling and foreground rules.
