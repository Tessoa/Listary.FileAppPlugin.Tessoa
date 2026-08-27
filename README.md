# Listary.FileAppPlugin.Tessoa

[Listary](https://www.listary.com/) file application plugin for
[tessoa](https://github.com/Tessoa), a GPU-accelerated file manager for Windows.

With this plugin, Listary knows which folder tessoa is showing. Quick Switch then jumps file
dialogs to it, and the folder appears in the *Currently Opened Folders* menu.

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

- A tessoa build that serves the query pipe. Check with `tessoa.exe --print-cwd`.
- Listary with file application plugin support.
- .NET Framework 4.8.

## Build

```
dotnet build -c Release
```

Output: `Listary.FileAppPlugin.Tessoa\bin\Release\net48`

## Install

Copy the output directory into `<Listary>\FileAppPlugins`, restart Listary, then enable the plugin
under **Listary options → Integration**.

## Check without Listary

tessoa answers the same pipe from the command line:

```
tessoa.exe --print-cwd
```

Exit code `0` prints a path, `1` means there is nothing to report.

## Updating an installed plugin

Listary loads the plugin into its own process, so the DLL cannot be overwritten while it runs.
Quit Listary (the `Listary.Service` process does not hold it), copy, then start Listary again.

## Troubleshooting

Listary keeps warnings and above in its log, so this plugin's messages are normally invisible.
Put an empty file named `verbose.txt` next to the plugin DLL and restart Listary. `BindFileWindow`,
`GetCurrentTab` and every `GetCurrentFolder` answer are then written to

```
%APPDATA%\Listary\UserProfile\Cache\<id>\ListaryLog.txt
```

That separates "Listary never asked" from "we answered nothing". Delete the file to go quiet.

## Scope

Implemented: `IGetFolder` — tessoa as a source of opened folders.

Not implemented: `IOpenFolder` / `IOpenFolderAndSelectFile`. Jumping *into* tessoa goes through its
existing command line, not this pipe, which stays read-only.

## License

MIT
