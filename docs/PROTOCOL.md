# Bridge protocol (version 1)

The MCP server (`MCPForAlibre.Server.exe`, started by Claude over stdio) talks to the add-on inside Alibre over the named pipe `\\.\pipe\MCPForAlibre`. Only the current user can open the pipe.

Each message is one line of UTF-8 JSON ending in `\n`. The client sends one request and waits for its reply before sending the next.

```
→ {"id": 1, "method": "status", "params": {}}
← {"id": 1, "result": { ... }}
← {"id": 1, "error": "No open session is called 'x'."}
```

If the pipe doesn't answer within 3 s, the server starts Alibre (`AlibreLauncher`), or waits for one that is loading, until the pipe exists (it lists `\\.\pipe\` rather than connecting), then connects again.

The first request on every connection is `hello`, which the add-on answers off Alibre's thread: `{"protocol": 1, "addOn": "<version>"}`. The server refuses a different protocol number. Raise `ProtocolVersion` in both `PipeServer.cs` and `AlibreBridge.cs` when a method's meaning changes.

Every other method runs on Alibre's UI thread. A call Alibre doesn't answer within 120 s returns an error, but it stays queued and runs when Alibre is free. The client retries once only if the request couldn't be sent (a pipe left over from a closed Alibre), never after sending, because the call may already have run.

## Methods

Lengths are millimetres, converted with the design's model units (`DesignProperties.ModelUnits`). `session` is optional everywhere and means the topmost session when left out.

| Method | Params | Result |
|---|---|---|
| `status` | | `alibreVersion`, `sessions[]` (`name`, `kind`, `filePath`, `identifier`, `modelUnits`), `activeSession` |
| `open_file` | `path`, `show` | session info |
| `save` | `session` | session info; an error if it has never been saved |
| `close` | `session`, `save` | `closed` |
| `part_summary` | `session` | `features[]`, `sketches[]`, `designPlanes[]`, `designAxes[]`, `designPoints[]`, `bodies`, `hasErrors`, `volumeMm3` |
| `list_parameters` | `session` | `[{name, type, value, units, equation, comment, mm or degrees}]` (`value` in model units: cm for new parts) |
| `set_parameter` | `session`, `name`, `equation` or `value`, `regenerate` (default true) | the parameter |
| `create_parameter` | `session`, `name`, `equation` or `value`, `type` (distance, angle, count, scale), `comment` | the parameter |
| `assembly_tree` | `session`, `depth` | the root occurrence with nested `children` |
| `selection` | `session` | `[{kind, name, index, id, occurrence, displayName}]` (`index` for faces and edges in a part) |
| `create_part` | `name`, `sheetMetal` | session info |

### Geometry references

- **Faces and edges** are numbered by `list_topology`: every body's faces (then edges) in enumeration order, from 0. A feature can change the numbering, so list them again after each one.
- **Features on faces and edges** take `faces[]` and/or `edges[]`, and `useSelection` to add what the user has selected in Alibre.
- **A reference** is text: a design plane, axis or point name (as `part_summary` lists them), `face:N`, `edge:N`, or `figure:ID` (a figure in the sketch the method works on).
- **Angles** are in degrees here and converted to the radians the API takes (`Angle` in `Commands.Features.cs`).

### Features

| Method | Params | Result |
|---|---|---|
| `list_topology` | `session` | `faces[]` (`index`, `surface`, `min`, `max`, `centre`, `normal` for planes), `edges[]` (`index`, `curve`, `start`, `middle`, `end`, `faces[]`) |
| `extrude` | `session`, `sketch`, `depth`, `end` (depth, midPlane, throughAll, toNext, toGeometry; or the old `midPlane`/`throughAll` flags), `to`, `offset`, `cut`, `reverse`, `draftAngle`, `outwardDraft`, `name` | feature result: `name`, `type`, `hasError` |
| `revolve` | `session`, `sketch`, `axis` (reference), `angle` (default 360), `cut`, `name` | feature result |
| `sweep` | `session`, `sketch`, `path[]` (sketch names), `rigid`, `cut`, `name` | feature result |
| `loft` | `session`, `sketches[]`, `connectEnds`, `minimizeTwist`, `cut`, `name` | feature result |
| `fillet` | `session`, `radius`, `faces[]`, `edges[]`, `useSelection`, `tangentPropagate`, `name` | feature result |
| `chamfer` | `session`, `distance`, `kind` (equal, twoDistances, angleDistance), `distance2`, `angle`, `faces[]`, `edges[]`, `useSelection`, `tangentPropagate`, `name` | feature result |
| `shell` | `session`, `thickness`, `faces[]` (to remove), `useSelection`, `outward` (must be true: 27.0.1's API makes only outward shells), `name` | feature result |
| `draft` | `session`, `angle`, `neutralPlane` (reference), `faces[]`, `useSelection`, `outward`, `name` | feature result |
| `hole` | `session`, `sketch` (with points, on a face), `diameter`, `kind` (simple, drilled, counterbore, countersink), `depth`, `throughAll`, `drillAngle`, `counterboreDiameter`, `counterboreDepth`, `countersinkDiameter`, `countersinkAngle`, `reverse`, `name` | feature result |
| `offset_face` | `session`, `offset`, `faces[]`, `useSelection`, `name` | feature result |
| `suppress_feature`, `unsuppress_feature` | `session`, `feature` | `name`, `suppressed`, `hasError` |
| `delete_feature` | `session`, `feature` | `deleted` |
| `rename_feature` | `session`, `feature`, `newName` | `name` |

### Sketches and reference geometry

| Method | Params | Result |
|---|---|---|
| `create_sketch` | `session`, `plane` (reference) or `face` (index), `name` | `name` |
| `add_sketch_figures` | `session`, `sketch`, `figures[]`: `type` line, circle, rectangle, arc, arc3, point, ellipse, ellipticArc, spline, with `x1 y1 x2 y2 xm ym cx cy radius majorAxis ratio angle points[[x,y]] reference` | `added[]` (`type`, `ids[]`), `figures`, `closed` |
| `list_sketch_figures` | `session`, `sketch` | `figures[]` (`id`, null for construction figures; `type`, `reference`, coordinates in mm, angles in degrees), `constraints`, `dimensions`, `closed` |
| `delete_sketch_figures` | `session`, `sketch`, `ids[]` | `deleted`, `figures` |
| `add_sketch_constraint` | `session`, `sketch`, `type`, `ids[]` (figure references, below; symmetric is the two, then what they're symmetric about) | `constraints`, `designGeometry` |

Figure references in `ids[]` (constraints and dimensions): a figure ID; `ID.start`, `ID.end`, `ID.centre` for a line's, arc's or circle's points; `origin` for the sketch's origin; or design geometry by name (`X-Axis`, `Y-Axis`, `Z-Axis`, `Origin`, or added axes and points). Alibre refuses design geometry in sketch constraints, so an axis is brought in as a fixed construction line (200 mm long) and a point as a fixed construction point, found again on later calls (`Commands.SketchRefs.cs`).

| `add_sketch_dimension` | `session`, `sketch`, `kind` (linear, radial, diametric), `ids[]`, `value` or `equation` | `dimensions`, `parameter` |
| `analyze_sketch` | `session`, `sketch`, `heal`, `tolerance` | `disjointEnds`, `openLoops`, `overlaps`, `intersections`, `degenerateFigures` (figure IDs) |
| `create_design_plane` | `session`, `kind` (offset, angle, threePoints), `from`, `offset`, `axis`, `angle`, `points[]`, `name` | `name` |
| `create_design_axis` | `session`, `kind` (twoPoints, twoPlanes, cylindricalFace), `points[]`, `planes[]`, `face`, `name` | `name` |
| `create_design_point` | `session`, `kind` (xyz, circularEdge, onEdge, between, threePlanes, offset), `x y z`, `edge`, `ratio`, `points[]`, `planes[]`, `from`, `dx dy dz`, `name` | `name`, `at` |

### Assemblies

Occurrences are named by their path from the top (`assembly_tree`'s `path`): child names joined by `/`, such as `Pin<2>` or `Sub<1>/Pin<2>`; a name unique anywhere in the tree also works. Positions are mm and rotations degrees, about the assembly's X, then Y, then Z (`Placement.cs`), in the occurrence's parent.

| Method | Params | Result |
|---|---|---|
| `create_assembly` | `name` | session info |
| `add_occurrence` | `session`, `file` or `design` (an open session's name), `x y z rx ry rz` | occurrence info |
| `add_empty_occurrence` | `session`, `name`, `kind` (part, assembly), `sheetMetal`, `x y z rx ry rz` | occurrence info |
| `occurrence_info` | `session`, `occurrence` | `name`, `path`, `kind`, `design`, `filePath`, `hidden`, `suppressed`, `anchored`, `position`, `rotation`, `min`, `max` |
| `move_occurrence` | `session`, `occurrence`, `x y z rx ry rz` (absent ones kept), `relative` | occurrence info |
| `set_occurrence` | `session`, `occurrence`, `hidden`, `suppressed`, `anchored` | occurrence info |
| `add_assembly_constraint` | `session`, `type` (mate, align, orient, angle, tangentInside, tangentOutside, gear, rack, screw, fastener), `first` and `second` (`{occurrence, face \| edge \| plane \| axis \| point}`), `value` (mm or degrees; rack: pinion radius; screw: pitch; fastener: offset; with a limit, the least or most), `value2` (the most, for between), `limit` (fixed, atLeast, atMost, between), `reversed`, `name`, `ratio1` and `ratio2` (gear) | `name`, `type`, `suppressed`, `hasError`, `participants`, `limit` (only atLeast, atMost or between, as Alibre reports it), and `ratio1`, `ratio2` (gear), `pinionRadiusMm` (rack), `pitchMm` (screw), `offsetMm`, `minimumOffsetMm`, `maximumOffsetMm` (fastener) |

Gear, rack, screw, fastener and any limit go through `AddConstraintEx`: with a limit, its own bound type; otherwise equality, then logical, then unknown, logged.
| `list_assembly_constraints` | `session` | constraints as above |
| `suppress_assembly_constraint` | `session`, `constraint`, `suppressed` | the constraint |
| `delete_assembly_constraint` | `session`, `constraint` | `deleted` |
| `check_interference` | `session`, `occurrences[]`, `against[]` (both optional) | `interferences[]` (`first`, `second`, `volumeMm3`, `min`, `max`) |

`list_topology` takes `occurrence` to list a part's faces and edges in the assembly's coordinates.

