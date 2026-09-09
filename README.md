# Listary.FileAppPlugin.Tessoa

[Listary](https://www.listary.com/) file application plugin for
[tessoa](https://github.com/Tessoa), a GPU-accelerated file manager for Windows.

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

## Requirements

- tessoa 0.22.0 or later. Check with `tessoa.exe --print-cwd`.
- For opening folders and selecting files, use tessoa 0.24.2 or later with **file application
  API 1**. The original 0.24.1 release only supports reading the current folder; updating this
  DLL alone cannot add opening or selection support.
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
`GetCurrentTab` and every `GetCurrentFolder` answer are then written to

```
%APPDATA%\Listary\UserProfile\Cache\<id>\ListaryLog.txt
```

That separates "Listary never asked" from "we answered nothing". Delete the file to go quiet.

## Scope

Implemented: `IGetFolder`, plus application-level `IOpenFolder` / `IOpenFolderAndSelectFile`.
Opening uses `tessoa.exe "--open-window=<folder>"`; selection uses
`tessoa.exe "--reveal-window=<file-or-folder>"`. (The flag names are historical: tessoa decides
between reusing a tab, opening one, and creating a window — see above.) Selection waits for a
slow directory to load and works in List, Icons, Gallery and Columns views; jumping to a tab
whose listing is stale refreshes it first, so a file that was just downloaded is still found.
Items excluded by tessoa's visibility rules remain unselected. Missing paths are rejected.

The current-path pipe stays read-only. Enumerating all tabs and the window- and tab-level open
interfaces (targeting one exact window, pane or tab) are not implemented; they would need
tessoa's forwarding protocol to carry a window target. `IsQuickSwitchTarget` remains false:
this is a file manager, not a file dialog.

## Regression checks

```
dotnet build tests/LauncherTests/LauncherTests.csproj -c Release
tests\LauncherTests\bin\Release\net48\LauncherTests.exe
```

The tests check Windows argument quoting (including roots, Unicode and spaces), actual child
process arguments, invalid paths and capability rejection without starting an older program.

## License

MIT
