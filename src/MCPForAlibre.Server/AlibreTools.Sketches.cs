using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MCPForAlibre.Server;

// Sketch and reference geometry tools.
public sealed partial class AlibreTools
{
    const string FigureRefsHelp = "What it applies to: figure IDs (add_sketch_figures, list_sketch_figures); a line's or " +
        "arc's end points as \"ID.start\" and \"ID.end\", and a centre as \"ID.centre\"; \"origin\" for the sketch's origin; " +
        "or built-in and added design geometry by name, such as X-Axis, Y-Axis, Z-Axis or Origin (part_summary lists it). " +
        "Alibre can't constrain to design geometry directly, so a design axis or point is added to the sketch as a fixed " +
        "construction line or point (reference true, no ID), reused on later calls.";

    [McpServerTool(Name = "create_sketch")]
    [Description("Add an empty 2D sketch to a part, on a design plane (part_summary lists them, such as XY-Plane) or a flat face.")]
    public Task<string> CreateSketch(
        [Description("Design plane name, or face:N.")] string? plane = null,
        [Description("Or a flat face's index from list_topology.")] int? face = null,
        [Description("Sketch name; omit for Alibre's default.")] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        plane is null && face is null
            ? throw new McpException("Give a plane or a face.")
            : Call("create_sketch", new { session, plane, face, name }, cancel);

    [McpServerTool(Name = "add_sketch_figures")]
    [Description("Add figures to a sketch, in mm in the sketch's own X and Y. Returns each figure's IDs " +
                 "(a rectangle gives four lines; construction figures get none), used by constraints, dimensions and revolve axes.")]
    public Task<string> AddSketchFigures(
        [Description("Sketch name.")] string sketch,
        [Description("Figures to add.")] SketchFigure[] figures,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        figures.Length == 0
            ? throw new McpException("Give at least one figure.")
            : Call("add_sketch_figures", new { session, sketch, figures }, cancel);

    [McpServerTool(Name = "list_sketch_figures", ReadOnly = true)]
    [Description("A sketch's figures with IDs, types, construction flags and coordinates in mm " +
                 "(line ends, centres, radii, arc ends and included angles in degrees), plus constraint and dimension counts. " +
                 "Construction figures have no ID in Alibre, so they list with id null.")]
    public Task<string> ListSketchFigures([Description("Sketch name.")] string sketch,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("list_sketch_figures", new { session, sketch }, cancel);

    [McpServerTool(Name = "delete_sketch_figures", Destructive = true)]
    [Description("Delete figures from a sketch by ID.")]
    public Task<string> DeleteSketchFigures([Description("Sketch name.")] string sketch,
        [Description("Figure IDs from list_sketch_figures.")] string[] ids,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("delete_sketch_figures", new { session, sketch, ids }, cancel);

    [McpServerTool(Name = "add_sketch_constraint")]
    [Description("Constrain sketch figures, also to built-in geometry: horizontal, vertical, coincident, parallel, " +
                 "perpendicular, tangent, equal, midpoint, collinear, coradial, symmetric (two figures or points, then the " +
                 "line or axis they're symmetric about), fix, normal or intersection. For example collinear [\"4\", \"X-Axis\"] " +
                 "puts line 4 on the X axis; symmetric [\"1\", \"3\", \"Y-Axis\"] centres lines 1 and 3 on the Y axis; " +
                 "coincident [\"2.start\", \"origin\"] puts a corner on the origin.")]
    public Task<string> AddSketchConstraint([Description("Sketch name.")] string sketch,
        [Description("Constraint type.")] string type,
        [Description(FigureRefsHelp)] string[] ids,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("add_sketch_constraint", new { session, sketch, type, ids }, cancel);

    [McpServerTool(Name = "add_sketch_dimension")]
    [Description("Dimension a sketch, which drives the geometry: linear (one line, or two points such as " +
                 "[\"2.start\", \"origin\"]), radial (a circle or arc) or diametric (a circle), to a value in mm or to an " +
                 "equation using parameters (create_parameter), such as \"Width\" or \"Height / 2\". Returns the dimension's parameter.")]
    public Task<string> AddSketchDimension([Description("Sketch name.")] string sketch,
        [Description(FigureRefsHelp)] string[] ids,
        [Description("Value in mm (or give equation).")] double? value = null,
        [Description("An equation, such as \"Width\" or \"Height * 2\", in Alibre's equation syntax.")] string? equation = null,
        [Description("linear (default), radial or diametric.")] string kind = "linear",
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default)
    {
        if (equation is null)
        {
            if (value is null)
                throw new McpException("Give a value or an equation.");
            RequirePositive(value.Value, "a value");
        }
        return Call("add_sketch_dimension", new { session, sketch, ids, value, equation, kind }, cancel);
    }

    [McpServerTool(Name = "analyze_sketch")]
    [Description("Check a sketch for problems that stop features: disjoint ends, open loops, overlaps, " +
                 "intersections and degenerate figures, by figure ID. heal closes small gaps.")]
    public Task<string> AnalyzeSketch([Description("Sketch name.")] string sketch,
        [Description("Repair what it can.")] bool heal = false,
        [Description("Healing tolerance in mm (default 0.01).")] double tolerance = 0.01,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("analyze_sketch", new { session, sketch, heal, tolerance }, cancel);

    [McpServerTool(Name = "create_design_plane")]
    [Description("Add a design plane: offset (from a plane or flat face by offset mm), angle (from a plane, about an axis, " +
                 "by angle degrees) or threePoints (through three design points).")]
    public Task<string> CreateDesignPlane(
        [Description("offset, angle or threePoints.")] string kind,
        [Description("For offset and angle: a design plane name or face:N.")] string? from = null,
        [Description("For offset: distance in mm.")] double? offset = null,
        [Description("For angle: a design axis name or edge:N.")] string? axis = null,
        [Description("For angle: degrees.")] double? angle = null,
        [Description("For threePoints: three design point names.")] string[]? points = null,
        [Description("Plane name; omit for Alibre's default.")] string? name = null,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("create_design_plane", new { session, kind, from, offset, axis, angle, points, name }, cancel);

    [McpServerTool(Name = "create_design_axis")]
    [Description("Add a design axis: twoPoints (through two design points), twoPlanes (where two planes meet) or " +
                 "cylindricalFace (a cylinder's axis).")]
    public Task<string> CreateDesignAxis(
        [Description("twoPoints, twoPlanes or cylindricalFace.")] string kind,
        [Description("For twoPoints: two design point names.")] string[]? points = null,
        [Description("For twoPlanes: two design plane names or face:N.")] string[]? planes = null,
        [Description("For cylindricalFace: the face's index from list_topology.")] int? face = null,
        [Description("Axis name; omit for Alibre's default.")] string? name = null,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("create_design_axis", new { session, kind, points, planes, face, name }, cancel);

    [McpServerTool(Name = "create_design_point")]
    [Description("Add a design point: xyz (mm), circularEdge (an arc's centre), onEdge (at a ratio along it), between " +
                 "(two points at a ratio), threePlanes (where they meet) or offset (from a point by dx, dy, dz mm).")]
    public Task<string> CreateDesignPoint(
        [Description("xyz, circularEdge, onEdge, between, threePlanes or offset.")] string kind,
        double? x = null, double? y = null, double? z = null,
        [Description("For circularEdge and onEdge: the edge's index from list_topology.")] int? edge = null,
        [Description("For onEdge and between: 0 to 1 (default 0.5).")] double? ratio = null,
        [Description("For between: two design point names.")] string[]? points = null,
        [Description("For threePlanes: three design plane names or face:N.")] string[]? planes = null,
        [Description("For offset: a design point name.")] string? from = null,
        double? dx = null, double? dy = null, double? dz = null,
        [Description("Point name; omit for Alibre's default.")] string? name = null,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("create_design_point", new { session, kind, x, y, z, edge, ratio, points, planes, from, dx, dy, dz, name }, cancel);
}

[Description("A sketch figure, in mm. line: x1,y1 to x2,y2. circle: cx,cy and radius. rectangle: corners x1,y1 and x2,y2. " +
             "arc: centre cx,cy from start x1,y1 anticlockwise to end x2,y2. arc3: start x1,y1 through xm,ym to end x2,y2. " +
             "point: x1,y1. ellipse: centre cx,cy, majorAxis, ratio (minor/major), angle of the major axis in degrees. " +
             "ellipticArc: as ellipse, from x1,y1 to x2,y2. spline: through points [[x,y],...]. " +
             "reference true makes construction geometry.")]
public sealed record SketchFigure(
    [property: Description("line, circle, rectangle, arc, arc3, point, ellipse, ellipticArc or spline.")] string type,
    double? x1 = null, double? y1 = null, double? x2 = null, double? y2 = null,
    double? xm = null, double? ym = null,
    double? cx = null, double? cy = null, double? radius = null,
    double? majorAxis = null, double? ratio = null, double? angle = null,
    double[][]? points = null,
    bool? reference = null);
