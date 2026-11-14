# MCPForAlibre

An MCP server for Alibre Design 27.0.1, so Claude and other MCP clients can read and build parts and assemblies.  Another compatibility project as I'm a bit behind in my Alibre version.

It has two parts:

- **The add-on**, `MCPForAlibre.AddOn.dll` (.NET Framework 4.8, x64). Alibre loads it at startup. It serves the named pipe `\\.\pipe\MCPForAlibre` and runs each request on Alibre's UI thread, where its COM objects live. It adds a menu item, MCPForAlibre > Status.
- **The MCP server**, `MCPForAlibre.Server.exe` (.NET 9). Claude starts it over stdio. It uses the official MCP C# SDK (`ModelContextProtocol`) and forwards each tool call to the add-on. The protocol between the two is in [docs/PROTOCOL.md](docs/PROTOCOL.md).

They're kept apart because Alibre runs on .NET Framework 4.8, while the SDK and its dependencies belong in a modern .NET process.

## Requirements

- Alibre Design 27.0.1.27039, installed in `C:\Program Files\Alibre Design 27.0.1.27039` (override with `-p:AlibreProgram=<folder>`)
- .NET SDK 9 and the .NET Framework 4.8 targeting pack (Visual Studio 2022 has both)

## Build, test, install

```
dotnet build MCPForAlibre.sln -c Release
dotnet test tests\MCPForAlibre.Tests -c Release
powershell -ExecutionPolicy Bypass -File deploy\install.ps1
```

Close Alibre before installing, and restart it afterwards. The script builds, asks for administrator rights, copies the add-on to `C:\ProgramData\Alibre AddOns\MCPForAlibre\` with the server in its `server\` folder, and registers it under `HKLM\SOFTWARE\Alibre Design Add-Ons`.

Then add the server to Claude Code:

```
claude mcp add --scope user alibre -- "C:\ProgramData\Alibre AddOns\MCPForAlibre\server\MCPForAlibre.Server.exe"
```

## Tools

Lengths are in millimetres and angles in degrees.

- Sessions: `alibre_status`, `open_file`, `save_session`, `close_session`, `create_part`
- Reading: `part_summary`, `list_topology` (faces and edges by index), `list_parameters`, `assembly_tree`, `get_selection`
- Parameters and equations: `create_parameter` (such as Width = 10), `set_parameter` (a value or an equation such as `Width * 2`); `add_sketch_dimension` takes an equation, so parameters drive sketches
- Features: `extrude` (depth, mid-plane, through all, to next, to geometry, draft), `revolve`, `sweep`, `loft`, `fillet`, `chamfer`, `shell` (outward only: Alibre 27.0.1's API refuses inward shells), `draft`, `hole` (simple, drilled, counterbore, countersink), `offset_face`, `suppress_feature`, `unsuppress_feature`, `delete_feature`, `rename_feature`
- Sketches: `create_sketch` (on a plane or face), `add_sketch_figures` (line, circle, rectangle, arc, 3-point arc, point, ellipse, elliptic arc, spline, construction), `list_sketch_figures`, `delete_sketch_figures`, `add_sketch_constraint`, `add_sketch_dimension` (both also to built-in geometry such as X-Axis, Y-Axis and the origin, and to line ends and centres such as `3.start`), `analyze_sketch`
- Reference geometry: `create_design_plane`, `create_design_axis`, `create_design_point`
- Assemblies: `create_assembly`, `add_occurrence` (from a file or an open part), `add_empty_occurrence`, `occurrence_info`, `move_occurrence`, `set_occurrence` (hidden, suppressed, anchored), `add_assembly_constraint` (mate, align, orient, angle, tangent, gear, rack, screw, fastener; with limits at least, at most or between), `list_assembly_constraints`, `suppress_assembly_constraint`, `delete_assembly_constraint`, `check_interference`; `list_topology` and `get_selection` work on an occurrence's part

Alibre 27.0.1's API has no pattern or mirror calls.

## Starting Alibre

When a call finds Alibre closed, the server starts it (the newest Alibre Design in Windows' install records) and waits up to 3 minutes for the add-on, then carries on with the call. An Alibre that is still loading is waited for, not started again, and only one server starts it at a time. If Claude gives up on a slow start, the next call waits for the same Alibre. Set `MCPFORALIBRE_ALIBRE_EXE` to the `Alibre Design.exe` to use, or `MCPFORALIBRE_NO_LAUNCH=1` to leave starting Alibre to you. Starts, waits and stops are logged in `%LOCALAPPDATA%\MCPForAlibre\MCPForAlibre.Server.log`.

## Tray icon

Each running server (one per Claude session) shows the MCPForAlibre icon in the notification area. Clicking it opens a window with the icon and:
- **Calls**: calls, errors, and the average and longest time for each add-on method, plus the last 200 calls with their results, errors in red.
- **Log**: the add-on's log, updated as it grows, with Open log file.

Closing the window hides it; the server keeps running for Claude. The icon's menu has Open, Open log file and Close server, which asks first, then stops the server: that Claude session loses its Alibre tools until Claude starts the server again. The tooltip shows the call and error counts. Set `MCPFORALIBRE_NO_TRAY=1` to run without it. The icon is drawn by `tools\MakeIcon.ps1`.

## Files

| What | Where |
|---|---|
| Log | `%LOCALAPPDATA%\MCPForAlibre\MCPForAlibre.log` |
| Installed add-on | `C:\ProgramData\Alibre AddOns\MCPForAlibre\` |
| Add-on entry points | `src\MCPForAlibre.AddOn\AlibreAddOn.cs` (`AlibreAddOnAssembly.AlibreAddOn`, the shape Alibre expects from a managed add-on) |
| Bridge methods | `src\MCPForAlibre.AddOn\Commands*.cs` (helpers in `Commands.cs`; `.Sessions`, `.Features`, `.Sketches`, `.Geometry`) |
| MCP tools | `src\MCPForAlibre.Server\AlibreTools*.cs` |
