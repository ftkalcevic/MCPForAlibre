using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MCPForAlibre.Server;

// Assembly tools.
public sealed partial class AlibreTools
{
    const string OccurrenceHelp = "The occurrence's path from assembly_tree, such as Bracket<1> or Sub<1>/Pin<2> " +
                                  "(a name that's unique anywhere in the assembly also works).";
    const string PoseHelp = "x, y, z in mm and rx, ry, rz in degrees: turned about the assembly's X, then Y, then Z, then moved.";

    [McpServerTool(Name = "create_assembly")]
    [Description("Create a new, empty assembly in its own window. It isn't saved until save_session (with a folder for its first save).")]
    public Task<string> CreateAssembly([Description("Name of the new assembly.")] string name, CancellationToken cancel = default) =>
        Call("create_assembly", new { name }, cancel);

    [McpServerTool(Name = "add_occurrence")]
    [Description("Add a part or subassembly to an assembly, from a file or an open session, at a position and rotation. " + PoseHelp)]
    public Task<string> AddOccurrence(
        [Description("Absolute path of an .AD_PRT or .AD_ASM file.")] string? file = null,
        [Description("Or the name or identifier of an open, saved part or assembly (alibre_status lists them).")] string? design = null,
        double x = 0, double y = 0, double z = 0, double rx = 0, double ry = 0, double rz = 0,
        [Description("The assembly; " + SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        file is null && design is null
            ? throw new McpException("Give a file or a design.")
            : Call("add_occurrence", new { session, file, design, x, y, z, rx, ry, rz }, cancel);

    [McpServerTool(Name = "add_empty_occurrence")]
    [Description("Add a new, empty part or subassembly to an assembly, at a position and rotation. " + PoseHelp)]
    public Task<string> AddEmptyOccurrence(
        [Description("Name of the new part or subassembly.")] string name,
        [Description("part (default) or assembly.")] string kind = "part",
        [Description("Make a sheet metal part.")] bool sheetMetal = false,
        double x = 0, double y = 0, double z = 0, double rx = 0, double ry = 0, double rz = 0,
        [Description("The assembly; " + SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        Call("add_empty_occurrence", new { session, name, kind, sheetMetal, x, y, z, rx, ry, rz }, cancel);

    [McpServerTool(Name = "occurrence_info", ReadOnly = true)]
    [Description("An occurrence's path, kind, file, flags, position (mm) and rotation (degrees) in its parent, and its box in the assembly.")]
    public Task<string> OccurrenceInfo([Description(OccurrenceHelp)] string occurrence,
        [Description("The assembly; " + SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("occurrence_info", new { session, occurrence }, cancel);

    [McpServerTool(Name = "move_occurrence")]
    [Description("Place an occurrence at a position and rotation in its parent (values left out stay as they are), " +
                 "or with relative true move it by them. " + PoseHelp + " Constraints can stop or undo a move.")]
    public Task<string> MoveOccurrence(
        [Description(OccurrenceHelp)] string occurrence,
        double? x = null, double? y = null, double? z = null, double? rx = null, double? ry = null, double? rz = null,
        [Description("Move by the values instead of to them.")] bool relative = false,
        [Description("The assembly; " + SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        Call("move_occurrence", new { session, occurrence, x, y, z, rx, ry, rz, relative }, cancel);

    [McpServerTool(Name = "set_occurrence")]
    [Description("Hide or show, suppress or unsuppress, and anchor (fix in place) or free an occurrence.")]
    public Task<string> SetOccurrence(
        [Description(OccurrenceHelp)] string occurrence,
        bool? hidden = null, bool? suppressed = null, bool? anchored = null,
        [Description("The assembly; " + SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        hidden is null && suppressed is null && anchored is null
            ? throw new McpException("Give hidden, suppressed or anchored.")
            : Call("set_occurrence", new { session, occurrence, hidden, suppressed, anchored }, cancel);

    [McpServerTool(Name = "add_assembly_constraint")]
    [Description("Constrain two occurrences: mate (faces against each other, normals opposed), align (faces in line, " +
                 "or axes in line), orient (parallel), angle, tangentInside, tangentOutside, or gear (couples two " +
                 "rotations: the sides are the two rotation axes, such as each part's design axis or a cylindrical face, " +
                 "turning ratio1 : ratio2, such as the tooth counts; reversed flips the sense). Gear only couples " +
                 "rotation: hold each part on its axis with align constraints first. Each side is " +
                 "{occurrence, and one of face or edge (list_topology with that occurrence) or plane, axis or point " +
                 "(design geometry names in its part)}; leave out occurrence for the assembly's own planes and axes.")]
    public Task<string> AddAssemblyConstraint(
        [Description("mate, align, orient, angle, tangentInside, tangentOutside or gear.")] string type,
        [Description("The first side.")] ConstraintSide first,
        [Description("The second side.")] ConstraintSide second,
        [Description("Offset in mm, or the angle in degrees for angle (default 0). Not used by gear.")] double value = 0,
        [Description("Flip the constraint's direction (for gear, which way the second turns).")] bool reversed = false,
        [Description("Constraint name; omit for Type<n>.")] string? name = null,
        [Description("Gear only: the first side's share of the ratio, such as its tooth count (default 1).")] double? ratio1 = null,
        [Description("Gear only: the second side's share of the ratio (default 1).")] double? ratio2 = null,
        [Description("The assembly; " + SessionHelp)] string? session = null,
        CancellationToken cancel = default)
    {
        bool gear = string.Equals(type, "gear", StringComparison.OrdinalIgnoreCase);
        if (!gear && (ratio1 is not null || ratio2 is not null))
            throw new McpException("ratio1 and ratio2 are only for type gear.");
        if (ratio1 is <= 0 || ratio2 is <= 0)
            throw new McpException("ratio1 and ratio2 must be greater than 0.");
        return Call("add_assembly_constraint", new { session, type, first, second, value, reversed, name, ratio1, ratio2 }, cancel);
    }

    [McpServerTool(Name = "list_assembly_constraints", ReadOnly = true)]
    [Description("An assembly's constraints: name, type, suppressed, errors and what each joins (and ratio1, ratio2 for gear).")]
    public Task<string> ListAssemblyConstraints([Description("The assembly; " + SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        Call("list_assembly_constraints", new { session }, cancel);

    [McpServerTool(Name = "suppress_assembly_constraint")]
    [Description("Suppress an assembly constraint, or bring it back with suppressed false.")]
    public Task<string> SuppressAssemblyConstraint([Description("Constraint name.")] string constraint,
        bool suppressed = true,
        [Description("The assembly; " + SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("suppress_assembly_constraint", new { session, constraint, suppressed }, cancel);

    [McpServerTool(Name = "delete_assembly_constraint", Destructive = true)]
    [Description("Delete an assembly constraint.")]
    public Task<string> DeleteAssemblyConstraint([Description("Constraint name.")] string constraint,
        [Description("The assembly; " + SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("delete_assembly_constraint", new { session, constraint }, cancel);

    [McpServerTool(Name = "check_interference", ReadOnly = true)]
    [Description("Find where parts overlap: each pair, the overlap's volume in mm^3 and its box. With no occurrences, " +
                 "the whole assembly; with occurrences only, among them; with against too, the first group against the second.")]
    public Task<string> CheckInterference(
        [Description("Occurrence paths to check (optional).")] string[]? occurrences = null,
        [Description("Occurrence paths to check them against (optional).")] string[]? against = null,
        [Description("The assembly; " + SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        Call("check_interference", new { session, occurrences, against }, cancel);
}

[Description("One side of an assembly constraint: an occurrence and one geometry item in it.")]
public sealed record ConstraintSide(
    [property: Description("Occurrence path; leave out for the assembly's own design geometry.")] string? occurrence = null,
    [property: Description("A face index from list_topology with this occurrence.")] int? face = null,
    [property: Description("An edge index from list_topology with this occurrence.")] int? edge = null,
    [property: Description("A design plane name, such as XY-Plane.")] string? plane = null,
    [property: Description("A design axis name, such as Z-Axis.")] string? axis = null,
    [property: Description("A design point name, such as Origin.")] string? point = null);
