# Listary.FileAppPlugin.Tessoa

[Listary](https://www.listary.com/) file application plugin for
[tessoa](https://github.com/Tessoa), a GPU-accelerated file manager for Windows.

> Building your own launcher or search tool? See
> [Integrating a launcher with tessoa](docs/launcher-integration.md)
> ([简体中文](docs/launcher-integration.zh-CN.md)) for the protocol this plugin uses.

With this plugin, Listary knows which folder tessoa is showing. Quick Switch then jumps file
dialogs to it, and the folder appears in the *Currently Opened Folders* menu.

Version 0.2.0 also implements opening a folder, and opening a file's parent folder with that
file selected, when Listary calls the plugin in a recognized tessoa window's search context.
Listary's global launcher uses its own opening settings instead; installing this plugin does
not make tessoa the destination of global search results.

tessoa lands these the same way its own "go to" actions do: if the folder is already open in a
tab it just focuses that tab (in any window), otherwise it opens a new tab in the focused tab
group. A window is created only when tessoa is not running. Repeating the same jump therefore
does not pile up duplicate tabs, and existing windows and tabs are never disturbed.

Version 0.3.0 adds the tab and window levels:

- **Jumping from a tessoa window moves the current tab.** Picking a folder in the search bar
  attached to tessoa navigates the tab in place, the way a folder click inside tessoa does, and
  Back returns to where it was. Revealing a file works the same way. A tab that cannot show a
  folder, such as a preview, gets a new tab in the same group instead.
- **Every tab can be listed.** `IGetFileTabs` returns all file tabs of a tessoa window, the
  current one first.
- **Window-level opening stays in that window.** It switches to a tab of that window that
  already shows the folder, or opens one in its focused tab group.

These need file application API 2 in the running tessoa, available since tessoa 0.31.2 (see
Requirements). Against an older tessoa the plugin behaves as 0.2.0 did.

### What Listary 6.3.5 actually calls

Checked against Listary 6.3.5.94:

- Listary only ever works with the current tab. It asks for it with `GetCurrentTab` and acts
  on that tab. It does not call `IGetFileTabs`, so *Currently Opened Folders* shows the
  current tab of each tessoa window, as it does for every other file manager plugin.
- For opening, Listary picks one level and sticks to it: tab if the plugin implements it,
  otherwise window, otherwise application. This plugin implements the tab level, so that is
  the one Listary uses.

The window level, the application level and tab listing are still implemented. They follow
the SDK, cost nothing when unused, and other hosts may call them.

## How it works

tessoa draws its own UI: no UI Automation tree, no standard controls, nothing to read a path out
of. So it exposes one instead — every tessoa process serves a read-only named pipe:

```
\\.\pipe\tessoa.q.<pid>
```

The plugin turns a window handle into a process id and asks that pipe.

Request and response frames are both `4-byte little-endian length + UTF-8 body`.

```
v 1
op query-cwd
hwnd 123456        # optional; omit to ask the focused window
```

A zero-length response means *no folder to report* — start page, This PC, a preview or terminal
tab, or an unknown window handle. It is a normal answer, not an error; the plugin returns an empty
string and never guesses.

`op query-tabs` lists every file tab of the window, the current one first:

```
api 2
tab 17 c D:\Work
tab 23 b C:\Users\me\Downloads
```

`c` marks the current tab, `v` the front tab of another tab group, `b` a background tab. The
number is tessoa's id for the tab; the plugin passes it back to move that exact tab. The `api`
line is always present, even with no tabs. An older tessoa answers this request with an empty
frame, which is how the plugin tells the two apart without launching anything.

## Requirements

- tessoa 0.22.0 or later. Check with `tessoa.exe --print-cwd`.
- For opening folders and selecting files, use tessoa 0.24.2 or later with **file application
  API 1**. The original 0.24.1 release only supports reading the current folder; updating this
  DLL alone cannot add opening or selection support.
- For listing every tab and for window- and tab-level opening, use tessoa 0.31.2 or later,
  which reports **file application API 2** (the `api` line above). The PE `Comments` marker stays
  `tessoa-file-app-api=1` so that plugin 0.2.0 keeps working with newer tessoa builds.
- Listary with file application plugin support.
- .NET Framework 4.8.

## Build

```
dotnet build -c Release
```

Output: `Listary.FileAppPlugin.Tessoa\bin\Release\net48`

## Install

Download the [latest release](https://github.com/Tessoa/Listary.FileAppPlugin.Tessoa/releases/latest)
and unzip it, or build it yourself as above. Copy the `Listary.FileAppPlugin.Tessoa` folder into
`<Listary>\FileAppPlugins`, restart Listary, then enable the plugin under
**Listary options → Integration**.

The plugin locates tessoa through a previously bound window, Windows App Paths, or
`%LOCALAPPDATA%\tessoa\tessoa.exe`. For a portable installation, put the full path to
`tessoa.exe` on one line in `tessoa-path.txt` next to the plugin DLL. This explicit path takes
precedence, including when tessoa is not running. An invalid explicit path fails without
launching another installation.

The plugin checks the executable's PE `Comments` resource for `tessoa-file-app-api=1` before
using the new commands. Older builds keep working as Quick Switch sources. If an older
process is still running after replacing the executable, restart tessoa before opening results.

## Check without Listary

tessoa answers the same pipe from the command line:

```
tessoa.exe --print-cwd
```

Exit code `0` prints a path, `1` means there is nothing to report.

## Global search setup

In Listary 6.3.5.94, the global launcher and standalone file search use **Open Folder With**
rather than this plugin's opening interfaces. The search context attached to a recognized
tessoa window can use the plugin interfaces. Explorer replacement is a separate integration
and does not demonstrate that Listary called the plugin.

For folder results in global search, set both fields under **Options → General → Open Folder
With** ([Listary documentation](https://help.listary.com/options-general)):

| Field | Value |
| --- | --- |
| Path | The full path to the installed `tessoa.exe` |
| Parameter | `"--open-window=%1"` |

The default installation is `%LOCALAPPDATA%\tessoa\tessoa.exe`; expand `%LOCALAPPDATA%` to its
actual directory when entering the executable path. A path without a parameter is insufficient:
Listary falls back to the system folder handler when either field is empty.

For file selection in global search, add a separate action under **Options → Actions**
([Listary documentation](https://help.listary.com/options-actions)):

| Field | Value |
| --- | --- |
| Title | Reveal in tessoa |
| Path | The same full path to the installed `tessoa.exe` |
| Parameter | `"--reveal-window={action_path}"` |
| Hotkey scope | Listary, if assigning a shortcut |

Select a file result, press **Ctrl+O**, and choose **Reveal in tessoa**. The built-in global
**Open containing folder** action passes only the parent directory to **Open Folder With**;
it loses the selected file's path. Changing that setting's parameter to `--reveal-window`
therefore cannot fix file selection. The custom action must use `{action_path}`, including
the filename, rather than `{action_path:parent}`.

These settings invoke tessoa directly and do not require Explorer replacement. They also do
not exercise the plugin DLL. To test the plugin itself, use Listary's plugin DevTools to call
`IOpenFolder.OpenFolder` and `IOpenFolderAndSelectFile.OpenFolderAndSelectFile`, or use the
search context attached to a recognized tessoa window. A global **Enter** or **Ctrl+Enter**
test alone cannot establish whether the plugin works.

For either route, repeat a folder request to check tab reuse, then reveal a file with spaces
and non-ASCII characters in its name. Also reveal a file outside the initial visible rows:
the file must be selected and scrolled into view. Merely opening its parent directory is not
a successful reveal.

## Updating an installed plugin

Listary loads the plugin into its own process, so the DLL cannot be overwritten while it runs.
Quit Listary (the `Listary.Service` process does not hold it), copy, then start Listary again.
Replace the existing tessoa plugin folder instead of keeping two copies. If it contains
`tessoa-path.txt`, preserve that file and check that it still names the intended installation.

## Troubleshooting

Listary keeps warnings and above in its log, so this plugin's messages are normally invisible.
Put an empty file named `verbose.txt` next to the plugin DLL and restart Listary. `BindFileWindow`,
`GetCurrentTab`, `GetTabs`, every `GetCurrentFolder` answer and every open request with its
level (application, window or tab) are then written to

```
%APPDATA%\Listary\UserProfile\Cache\<id>\ListaryLog.txt
```

That separates "Listary never asked" from "we answered nothing". Delete the file to go quiet.

## Scope

Implemented: `IGetFolder` and `IGetFileTabs`, plus `IOpenFolder` / `IOpenFolderAndSelectFile`
at all three levels: application, window and tab. Listary 6.3.5 uses only the tab level (see
above).
Opening uses `tessoa.exe "--open-window=<folder>"`; selection uses
`tessoa.exe "--reveal-window=<file-or-folder>"`. (The flag names are historical: tessoa decides
between reusing a tab, opening one, and creating a window — see above.) Selection waits for a
slow directory to load and works in List, Icons, Gallery and Columns views; jumping to a tab
whose listing is stale refreshes it first, so a file that was just downloaded is still found.
Items excluded by tessoa's visibility rules remain unselected. Missing paths are rejected.

Window and tab levels add one argument to the same command line: `--in-window=<hwnd>`, or
`--in-tab=<hwnd>` for the current tab and `--in-tab=<hwnd>:<tab id>` for a listed one. If the
window or tab is gone by the time tessoa handles the request, tessoa steps down a level (tab,
then window, then application) rather than doing nothing. If the running tessoa is older than
the executable the plugin found and rejects the scoped request (exit code 1), the plugin asks
once more without the scope.

The current-path pipe stays read-only. `IsQuickSwitchTarget` remains false: this is a file
manager, not a file dialog.

## Regression checks

```
dotnet build tests/LauncherTests/LauncherTests.csproj -c Release
tests\LauncherTests\bin\Release\net48\LauncherTests.exe
```

The tests check Windows argument quoting (including roots, Unicode and spaces), actual child
process arguments, invalid paths and capability rejection without starting an older program.
They also check that a scope travels as its own argument, that a rejected scoped request is
retried once without it, and how `query-tabs` answers are parsed (empty frame, missing `api`
line, malformed lines, paths with spaces).

## License

MIT
