# MCPForAlibre

An MCP server for Alibre Design 27.0.1.27039. It's named like CAMForAlibre (`D:\Projects\alibre\AlibreCAM`) so it doesn't sound like an Alibre product. `README.md` covers the build, install and files. `docs\PROTOCOL.md` is the pipe protocol between the server and the add-on.

## Decisions (Frank's)

- C# / .NET, not C++.
- The server starts Alibre on any call that finds it closed (`AlibreLauncher`), and Claude keeps starting the server (stdio, one per session). The add-on doesn't start the server: one started by Alibre would have no Claude connected, and serving HTTP instead would lose starting Alibre.
- The MCP SDK runs out of process: a .NET 9 stdio server (`src\MCPForAlibre.Server`, the official `ModelContextProtocol` package) that Claude starts. It talks over the named pipe `\\.\pipe\MCPForAlibre` to a slim add-on (`src\MCPForAlibre.AddOn`, net48 x64) loaded by Alibre. The SDK isn't loaded into Alibre because its dozen dependency DLLs would need binding redirects that can't be added to Alibre's config.

## Working with Frank

- Frank commits and pushes himself. Don't commit unless he asks.
- Frank tests in Alibre by hand. End a change with a short numbered list of what to test, and say plainly what hasn't been built or run.
- Check the log (`%LOCALAPPDATA%\MCPForAlibre\MCPForAlibre.log`) before asking Frank what happened.
- Ask before making a product decision that isn't recorded here. Propose options when you do.

## Alibre API

- Reference with `dotnet build`: Alibre's own interop assemblies `AlibreX.dll` and `AlibreAddOn.dll` from its `Program` folder (`Private` false). They're the authority for 27.0.1. The docs in `C:\AlibreToolBox\MyAddon - Claude Reference\...\Reference\` describe v29 and are read-only.
- Managed add-ons: `.adc` with `<DLL type="Managed" ...>`, entry class `AlibreAddOnAssembly.AlibreAddOn` with `AddOnLoad(IntPtr, IAutomationHook, IntPtr)`, `AddOnInvoke`, `AddOnUnload` and `GetAddOnInterface()` (copied from Alibre Script's DLL by reflection).
- Call AlibreX only on Alibre's UI thread (`UiThread.Run`). The pipe threads never touch COM.
- Lengths in the API are in model units (cm for new files); convert with `MmPerUnit` in `Commands.cs`.
- Proven API behaviour from CAMForAlibre is in `D:\Projects\alibre\AlibreCAM\CLAUDE.md`.

## Adding a tool

1. A method in the right `Commands.*.cs` partial, registered in that file's `Register...` method. Shared helpers are in `Commands.cs`: `FindPart`, `FindSketch`, `FindFeature`, `FindFigure`, `ResolveTopology` (faces/edges/useSelection), `ResolveReference` (names, `face:N`, `edge:N`, `figure:ID`), `MmPerUnit`, and `Angle` in `Commands.Features.cs`.
2. A `[McpServerTool]` in the matching `AlibreTools.*.cs` that calls it. Check the inputs there and throw `McpException` before calling Alibre.
3. A row in `docs\PROTOCOL.md` and the tool list in `README.md`.

Agents only learn what the server tells them: tool descriptions, parameters, error messages, and the instructions sent when a client connects (`src\MCPForAlibre.Server\ServerInstructions.cs`). They never see this file. So when Alibre turns out not to do something, or does it its own way, say so where an agent will read it: in the tool's description, in the error message (with what to do instead), and, if it affects more than one tool or a whole kind of task, in `ServerInstructions.cs`.

## Current state

Built, installed, and registered in Claude Code's user config as `alibre`. 47 tools (see `README.md`). The server shows a tray icon; its window has the calls, timings and errors of that server and the add-on log (`TrayWindow.cs`, `ServerStats.cs`; `MCPFORALIBRE_NO_TRAY=1` turns it off), and its menu has Close server (asks first). The server ends when its parent process goes (`ParentWatch.cs`: only a parent seen running; Claude's session starts it through a launcher that exits at once, so a parent already gone at startup is ignored, which an earlier version got wrong and closed straight away), or when stdin closes. When a call finds Alibre closed, `AlibreLauncher` starts the newest Alibre Design from Windows' uninstall records (`MCPFORALIBRE_ALIBRE_EXE` overrides, `MCPFORALIBRE_NO_LAUNCH=1` turns it off), one server at a time (mutex `Local\MCPForAlibre.StartingAlibre`), and waits up to 3 minutes from Alibre's start for the pipe. Proven on 2026-11-10 from the build folder: two servers calling at once with Alibre closed started one Alibre (the other waited), its add-on answered after 10 s, and both calls returned in about 15 s. Its own log is `%LOCALAPPDATA%\MCPForAlibre\MCPForAlibre.Server.log` (`ServerLog.cs`: starts, the parent, why it stopped). The installer replaces only the server while Alibre runs, and only the add-on while a server runs. The icon is drawn by `tools\MakeIcon.ps1`.

Proven in Alibre on 2026-11-10, through the installed server, each feature checked against a volume worked out by hand:
- Sessions, `part_summary`, `create_part`, `list_topology`, sketches on planes and faces, rectangles, circles, arcs, 3-point arcs, points, ellipses, splines, construction lines, deleting figures, horizontal constraint, linear and diametric dimensions, `analyze_sketch`.
- Extrude (depth, to a design plane), revolve (360 and 90 degrees about Y-Axis), sweep (a circle along a line and arc, path as sketches), loft, fillet, chamfer, draft, offset face, outward shell, holes (simple, drilled, countersink, counterbore), suppress, unsuppress, rename, delete, default names.
- Design planes (offset, angle, 3 points), axes (cylindrical face, 2 planes, 2 points) and points (xyz, circular edge, on edge, between, 3 planes, offset).

Built-in geometry in sketches, proven on 2026-11-10: a rectangle drawn at (3, 5)-(20, 40), then collinear [lower edge, X-Axis], symmetric [left, right, Y-Axis], width 10 and height 30, came to corners (-5, 0), (5, 0), (5, 30), (-5, 30), with both axis lines still fixed; a line's start coincident with `origin` and a two-point dimension of 25 between its `start` and `end`.

Equations, proven on 2026-11-10: `create_parameter` Width = 10 and Height = 20 (read back as 10 and 20 mm), a 30 x 15 rectangle driven by dimensions with equations `Width` and `Height` to 10 x 20, extruded 5 mm to 1000 mm^3, then `set_parameter` Width 15 (1500 mm^3) and Height `Width * 2` (2250 mm^3).

Assemblies, proven on 2026-11-10 against worked-out numbers (two saved blocks): `create_assembly`, `add_occurrence` from an open saved part, placing at x 100, an absolute move with a 90 degree turn about Z (box as expected), a relative move, `check_interference` (one overlap of 2000 mm^3 in the right box, whole assembly and A against B), `set_occurrence` anchored, hidden, suppressed, `list_topology` with an occurrence, a mate of A's top to B's bottom (B seated at z 10, no overlap after), listing, suppressing and deleting constraints, `add_empty_occurrence`, `assembly_tree` with paths and positions, and `save_session` with a folder.

Tested by Frank on 2026-11-10: the tray icon and window (Calls and Log tabs, closing hides it and the server carries on), the add-on's icon in Alibre, `get_selection` giving a picked face's index, and a fillet with `useSelection`.

Alibre 27.0.1 behaviour found then (the code works around each):
- Angles are radians (`Angle` converts). Sketch angles are listed in degrees.
- `IObjectCollector` doesn't enumerate in .NET; read it with `ItemsOf` (by index).
- A refused feature can stay in the part with an error ("Feature is created and is available in features list"); `DeleteIfLeft` removes it.
- Listing features after reading faces left the faces stale ("Object no longer exists in server"), so `FeatureName` runs first.
- Unnamed features got wrong default names (a fillet became `Sweep<2>`), so `FeatureName` names them `Kind<n>`.
- Offset face refuses an empty parameter name; `UnusedParameterName` gives one.
- Holes need the sketch on a face, not a design plane. Counterbore throws a null reference through all (Ex and older calls); it works as to-depth 1000 mm.
- Shell: the per-face thickness arguments can't be empty; one kept face at the standard thickness is accepted. Every inward shell is refused (VALUE_NEGATIVE), so the tool makes outward shells only.
- `AddCircularArcBy3Points` takes centre, start, end; `arc3` works out the centre itself.
- Constraints, dimensions and `Analyze` need `BeginChange`/`EndChange`.
- Construction figures and line end points have no ID, so a linear dimension between two points can't be asked for by ID.
- Parameters: every change needs an open parameter transaction ("No opened transaction found"), but `NewParameter` opens its own and fails inside one ("Parameter transaction already open"). `CreateParameter` makes the parameter first, then sets its equation through `ParameterChange`, which opens a transaction only if none is open and closes or cancels only its own. A bare number in an equation is in the document's units ("10" was 10 mm); `IADParameter.Value` is in model units (cm). A sketch dimension is driven by an equation by placing it at the figure's size, then setting its `Parameter.Equation`.
- Sketch constraints refuse design axes and points ("Unsupported object passed", 0x80040322); `InSketch` brings them in as fixed construction geometry. The sketch's `OriginPoint` is taken directly. Symmetric wants the line of symmetry first (given last, Alibre mirrored the other figures about the first); the tool takes it last and reorders.
- Alibre only inserts saved designs: an unsaved part gives "Can't insert Occurrence" (0x80040242), so `add_occurrence` says to save it with `save_session` folder (`SaveNew`).
- `IADOccurrence.ApplyTransform` moves by the transform given (relative); `ApplyPlacement` works out the change from the local transform, and checks.
- Session names aren't unique (two unsaved parts can both be BlockA), so `SessionNamed` takes an identifier and refuses a shared name, listing the identifiers (proven 2026-11-10).
- Git Bash's Python can't open `\\.\pipe\MCPForAlibre`; test the pipe from PowerShell.

Gear constraints, added on 2026-11-10 (`type` gear, `ratio1`/`ratio2`, through `AddConstraintEx`; `ConstraintInfo` reports the ratios from `IADGearConstraint`): proven between two parts' design Z-Axes in `D:\Projects\alibre\camtest\examples\BevelGears\BevelGearPair.AD_ASM` (1:1, no error). Alibre took bound type `AD_EQUALITY_TYPE` first time; logical and unknown are tried after it if refused. Align with design axes and planes (to the assembly's X-Axis, Y-Axis, YZ-Plane, ZX-Plane) also worked there. A relative `move_occurrence` turn of a gear held by these constraints left both gears where they were, so whether the gear constraint drives the other part is untested (drag it in Alibre).

Still unproven: the Status menu, the assembly constraint types orient, angle and tangent, gear ratios other than 1:1, constraints to design planes, axes and points, `add_occurrence` from a file, `get_selection` in an assembly, other sketch constraint types, radial dimensions, cut variants of revolve, sweep and loft, extrude to next and with draft, angle parameters.
