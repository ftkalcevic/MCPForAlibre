using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MCPForAlibre.Server;

// Topology and part feature tools.
public sealed partial class AlibreTools
{
    const string FacesHelp = "Face indexes from list_topology. Indexes can change after any feature, so list again first.";
    const string EdgesHelp = "Edge indexes from list_topology. Indexes can change after any feature, so list again first.";
    const string SelectionHelp = "Also use the faces and edges the user has selected in Alibre.";
    const string ReferenceHelp = "A design plane, axis or point name (part_summary), face:N or edge:N (list_topology)";
    const string NameHelp = "Feature name; omit for Alibre's default.";

    static void RequireTargets(int[]? faces, int[]? edges, bool useSelection)
    {
        if ((faces is null || faces.Length == 0) && (edges is null || edges.Length == 0) && !useSelection)
            throw new McpException("Give faces or edges (indexes from list_topology), or useSelection.");
    }

    static void RequirePositive(double value, string name)
    {
        if (!(value > 0))
            throw new McpException($"Give {name} greater than 0.");
    }

    [McpServerTool(Name = "list_topology", ReadOnly = true)]
    [Description("A part's faces (index, surface type, centre, box, and outward normal for planes) and edges " +
                 "(index, curve type, start, middle and end points, and the faces they join), all in mm. " +
                 "Fillet, chamfer, shell, draft and others name faces and edges by these indexes. In an assembly, " +
                 "give an occurrence to list its part's faces and edges in the assembly's coordinates, for constraints.")]
    public Task<string> ListTopology([Description(SessionHelp)] string? session = null,
        [Description("In an assembly: the occurrence path of a part (assembly_tree).")] string? occurrence = null,
        CancellationToken cancel = default) =>
        Call("list_topology", new { session, occurrence }, cancel);

    [McpServerTool(Name = "extrude")]
    [Description("Extrude a closed sketch into a boss, or cut it out of the part.")]
    public Task<string> Extrude(
        [Description("Sketch name.")] string sketch,
        [Description("Depth in mm, for depth and midPlane.")] double depth = 0,
        [Description("depth (default), midPlane (half each side), throughAll, toNext, or toGeometry (up to 'to').")] string end = "depth",
        [Description("For toGeometry: " + ReferenceHelp + ".")] string? to = null,
        [Description("For toGeometry: offset in mm from it.")] double offset = 0,
        [Description("Cut instead of adding material.")] bool cut = false,
        [Description("Go the other way from the sketch plane.")] bool reverse = false,
        [Description("Draft angle in degrees.")] double draftAngle = 0,
        [Description("Draft outwards instead of inwards.")] bool outwardDraft = false,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default)
    {
        if ((end == "depth" || end == "midPlane") && !(depth > 0))
            throw new McpException("Give a depth greater than 0, or another end.");
        if (end == "toGeometry" && string.IsNullOrEmpty(to))
            throw new McpException("toGeometry needs 'to'.");
        return Call("extrude", new { session, sketch, depth, end, to, offset, cut, reverse, draftAngle, outwardDraft, name }, cancel);
    }

    [McpServerTool(Name = "revolve")]
    [Description("Revolve a closed sketch about an axis into a boss, or cut it out of the part.")]
    public Task<string> Revolve(
        [Description("Sketch name.")] string sketch,
        [Description("The axis: a design axis name (part_summary lists them), edge:N, or figure:ID for a line in this sketch.")] string axis,
        [Description("Angle in degrees (default 360).")] double angle = 360,
        [Description("Cut instead of adding material.")] bool cut = false,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default)
    {
        if (!(angle > 0 && angle <= 360))
            throw new McpException("The angle must be over 0 and at most 360 degrees.");
        return Call("revolve", new { session, sketch, axis, angle, cut, name }, cancel);
    }

    [McpServerTool(Name = "sweep")]
    [Description("Sweep a closed profile sketch along a path made of one or more other sketches.")]
    public Task<string> Sweep(
        [Description("Profile sketch name.")] string sketch,
        [Description("Path sketch names, in order.")] string[] path,
        [Description("Keep the profile's orientation fixed instead of following the path.")] bool rigid = false,
        [Description("Cut instead of adding material.")] bool cut = false,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        Call("sweep", new { session, sketch, path, rigid, cut, name }, cancel);

    [McpServerTool(Name = "loft")]
    [Description("Loft through two or more closed sketches, in order.")]
    public Task<string> Loft(
        [Description("Sketch names, in order.")] string[] sketches,
        [Description("Join the last section back to the first.")] bool connectEnds = false,
        [Description("Minimise twist between sections.")] bool minimizeTwist = true,
        [Description("Cut instead of adding material.")] bool cut = false,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        sketches.Length < 2
            ? throw new McpException("A loft needs at least two sketches.")
            : Call("loft", new { session, sketches, connectEnds, minimizeTwist, cut, name }, cancel);

    [McpServerTool(Name = "fillet")]
    [Description("Round edges (or all edges of faces) with a constant radius.")]
    public Task<string> Fillet(
        [Description("Radius in mm.")] double radius,
        [Description(EdgesHelp)] int[]? edges = null,
        [Description(FacesHelp)] int[]? faces = null,
        [Description(SelectionHelp)] bool useSelection = false,
        [Description("Carry on along tangent edges.")] bool tangentPropagate = true,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default)
    {
        RequirePositive(radius, "a radius");
        RequireTargets(faces, edges, useSelection);
        return Call("fillet", new { session, radius, edges, faces, useSelection, tangentPropagate, name }, cancel);
    }

    [McpServerTool(Name = "chamfer")]
    [Description("Chamfer edges (or all edges of faces).")]
    public Task<string> Chamfer(
        [Description("Distance in mm.")] double distance,
        [Description("equal (default), twoDistances (with distance2) or angleDistance (with angle).")] string kind = "equal",
        [Description("Second distance in mm, for twoDistances.")] double? distance2 = null,
        [Description("Angle in degrees, for angleDistance.")] double? angle = null,
        [Description(EdgesHelp)] int[]? edges = null,
        [Description(FacesHelp)] int[]? faces = null,
        [Description(SelectionHelp)] bool useSelection = false,
        [Description("Carry on along tangent edges.")] bool tangentPropagate = true,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default)
    {
        RequirePositive(distance, "a distance");
        RequireTargets(faces, edges, useSelection);
        return Call("chamfer", new { session, distance, kind, distance2, angle, edges, faces, useSelection, tangentPropagate, name }, cancel);
    }

    [McpServerTool(Name = "shell")]
    [Description("Hollow the part with a wall added outside it, removing the given faces (none gives a closed hollow). " +
                 "Alibre 27.0.1's API makes only outward shells, so the part grows by the thickness.")]
    public Task<string> Shell(
        [Description("Wall thickness in mm.")] double thickness,
        [Description("Faces to remove: " + FacesHelp)] int[]? faces = null,
        [Description("Also remove the faces the user has selected in Alibre.")] bool useSelection = false,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default)
    {
        RequirePositive(thickness, "a thickness");
        return Call("shell", new { session, thickness, faces, useSelection, outward = true, name }, cancel);
    }

    [McpServerTool(Name = "draft")]
    [Description("Tilt faces by an angle about a neutral plane.")]
    public Task<string> Draft(
        [Description("Angle in degrees.")] double angle,
        [Description("Neutral plane: a design plane name or face:N.")] string neutralPlane,
        [Description(FacesHelp)] int[]? faces = null,
        [Description(SelectionHelp)] bool useSelection = false,
        [Description("Draft outwards instead of inwards.")] bool outward = false,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default)
    {
        RequireTargets(faces, null, useSelection);
        return Call("draft", new { session, angle, neutralPlane, faces, useSelection, outward, name }, cancel);
    }

    [McpServerTool(Name = "hole")]
    [Description("Holes at the points of a sketch (add them with add_sketch_figures type point), into the face the sketch is on. " +
                 "The sketch must be on a face (create_sketch face), not a design plane.")]
    public Task<string> Hole(
        [Description("Sketch with the hole centres.")] string sketch,
        [Description("Diameter in mm.")] double diameter,
        [Description("simple (default), drilled (pointed bottom), counterbore or countersink.")] string kind = "simple",
        [Description("Depth in mm (not needed with throughAll).")] double depth = 0,
        [Description("Through the whole part (a counterbore is made 1000 mm deep instead, as Alibre refuses it through all).")] bool throughAll = false,
        [Description("Drill point angle in degrees, for drilled (default 118).")] double? drillAngle = null,
        [Description("Counterbore diameter in mm.")] double? counterboreDiameter = null,
        [Description("Counterbore depth in mm.")] double? counterboreDepth = null,
        [Description("Countersink diameter in mm.")] double? countersinkDiameter = null,
        [Description("Countersink angle in degrees (default 90).")] double? countersinkAngle = null,
        [Description("Go the other way from the sketch plane.")] bool reverse = false,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default)
    {
        RequirePositive(diameter, "a diameter");
        if (!throughAll)
            RequirePositive(depth, "a depth (or throughAll)");
        if (kind == "counterbore" && (counterboreDiameter is null || counterboreDepth is null))
            throw new McpException("A counterbore needs counterboreDiameter and counterboreDepth.");
        if (kind == "countersink" && countersinkDiameter is null)
            throw new McpException("A countersink needs countersinkDiameter.");
        return Call("hole", new { session, sketch, diameter, kind, depth, throughAll, drillAngle, counterboreDiameter,
                                  counterboreDepth, countersinkDiameter, countersinkAngle, reverse, name }, cancel);
    }

    [McpServerTool(Name = "offset_face")]
    [Description("Move faces along their normals by a distance (negative goes into the part).")]
    public Task<string> OffsetFace(
        [Description("Offset in mm.")] double offset,
        [Description(FacesHelp)] int[]? faces = null,
        [Description(SelectionHelp)] bool useSelection = false,
        [Description(NameHelp)] string? name = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default)
    {
        RequireTargets(faces, null, useSelection);
        return Call("offset_face", new { session, offset, faces, useSelection, name }, cancel);
    }

    [McpServerTool(Name = "suppress_feature")]
    [Description("Suppress a feature (it stays in the tree but has no effect).")]
    public Task<string> SuppressFeature([Description("Feature name.")] string feature,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("suppress_feature", new { session, feature }, cancel);

    [McpServerTool(Name = "unsuppress_feature")]
    [Description("Bring a suppressed feature back.")]
    public Task<string> UnsuppressFeature([Description("Feature name.")] string feature,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("unsuppress_feature", new { session, feature }, cancel);

    [McpServerTool(Name = "delete_feature", Destructive = true)]
    [Description("Delete a feature from the part.")]
    public Task<string> DeleteFeature([Description("Feature name.")] string feature,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("delete_feature", new { session, feature }, cancel);

    [McpServerTool(Name = "rename_feature")]
    [Description("Rename a feature.")]
    public Task<string> RenameFeature([Description("Feature name now.")] string feature, [Description("New name.")] string newName,
        [Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("rename_feature", new { session, feature, newName }, cancel);
}
