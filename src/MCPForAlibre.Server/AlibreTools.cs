using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MCPForAlibre.Server;

// The MCP tools. Each forwards to the add-on's method of the same name (docs\PROTOCOL.md)
// and returns its JSON result. Lengths are millimetres throughout.
[McpServerToolType]
public sealed partial class AlibreTools(AlibreBridge bridge, ServerStats stats)
{
    static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Every call is timed and counted for the tray window, by the add-on method's name.
    internal Task<string> Call(string method, object? parameters, CancellationToken cancel) =>
        stats.Measure(method, async () =>
        {
            try
            {
                var result = await bridge.CallAsync(method, parameters, cancel);
                return result?.ToJsonString(s_options) ?? "null";
            }
            catch (AlibreException ex)
            {
                throw new McpException(ex.Message); // shown to the client as the tool's error
            }
        });

    internal const string SessionHelp = "Session name as Alibre shows it on the window tab, or its identifier from alibre_status " +
                                        "(needed when two open sessions share a name); omit for the active session.";

    [McpServerTool(Name = "alibre_status", ReadOnly = true)]
    [Description("Alibre's version, every open session (name, kind, file path, model units) and which one is active.")]
    public Task<string> Status(CancellationToken cancel) => Call("status", null, cancel);

    [McpServerTool(Name = "open_file")]
    [Description("Open a part (.AD_PRT), assembly (.AD_ASM) or drawing (.AD_DRW) in Alibre.")]
    public Task<string> OpenFile(
        [Description("Absolute Windows path of the file.")] string path,
        [Description("Open it in a window (default true); false opens it without one.")] bool show = true,
        CancellationToken cancel = default) =>
        Call("open_file", new { path, show }, cancel);

    [McpServerTool(Name = "save_session")]
    [Description("Save a session to its file. With folder: save a session that has never been saved into that folder " +
                 "(with any new parts in it), or with name too, save a copy under that name. Assemblies can only insert saved parts.")]
    public Task<string> Save([Description(SessionHelp)] string? session = null,
        [Description("An existing folder, for a first save or a copy.")] string? folder = null,
        [Description("With folder: the copy's name.")] string? name = null,
        CancellationToken cancel = default) =>
        name is not null && folder is null
            ? throw new McpException("A new name needs a folder too.")
            : Call("save", new { session, folder, name }, cancel);

    [McpServerTool(Name = "close_session", Destructive = true)]
    [Description("Close a session. Unsaved changes are lost unless save is true.")]
    public Task<string> Close(
        [Description(SessionHelp)] string? session = null,
        [Description("Save before closing.")] bool save = false,
        CancellationToken cancel = default) =>
        Call("close", new { session, save }, cancel);

    [McpServerTool(Name = "part_summary", ReadOnly = true)]
    [Description("A part's features (type, suppressed, errors), sketches, design planes, axes and points, body count and volume in mm^3.")]
    public Task<string> PartSummary([Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("part_summary", new { session }, cancel);

    [McpServerTool(Name = "list_parameters", ReadOnly = true)]
    [Description("A session's parameters: name, type, value (as the API gives it), mm for distances, degrees for angles, " +
                 "units and equation.")]
    public Task<string> ListParameters([Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("list_parameters", new { session }, cancel);

    [McpServerTool(Name = "set_parameter")]
    [Description("Change a parameter, then regenerate. Prefer an equation with units, such as \"25 mm\" or \"D1 * 2\".")]
    public Task<string> SetParameter(
        [Description("Parameter name, such as D1.")] string name,
        [Description("New equation, such as \"25 mm\". Used in preference to value.")] string? equation = null,
        [Description("New value in the API's units (see list_parameters). Ignored when equation is given.")] double? value = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        equation is null && value is null
            ? throw new McpException("Give an equation or a value.")
            : Call("set_parameter", new { session, name, equation, value }, cancel);

    [McpServerTool(Name = "create_parameter")]
    [Description("Create a named parameter (an Alibre equation), such as Width = \"10 mm\" or Height = \"Width * 2\", " +
                 "which dimensions and other equations can then use by name.")]
    public Task<string> CreateParameter(
        [Description("Parameter name, such as Width.")] string name,
        [Description("Its equation, such as \"10 mm\", \"10\" (the document's units) or \"Width * 2\".")] string? equation = null,
        [Description("Or a value in the API's units. Ignored when equation is given.")] double? value = null,
        [Description("distance (default), angle, count or scale.")] string type = "distance",
        [Description("A comment shown in Alibre's equation editor.")] string? comment = null,
        [Description(SessionHelp)] string? session = null,
        CancellationToken cancel = default) =>
        equation is null && value is null
            ? throw new McpException("Give an equation or a value.")
            : Call("create_parameter", new { session, name, equation, value, type, comment }, cancel);

    [McpServerTool(Name = "assembly_tree", ReadOnly = true)]
    [Description("An assembly's occurrences as a tree: name, path (used by the assembly tools), kind, design, file, " +
                 "hidden, suppressed, anchored, position (mm) and rotation (degrees) in its parent, and its box.")]
    public Task<string> AssemblyTree(
        [Description(SessionHelp)] string? session = null,
        [Description("How many levels to go down (default 10).")] int depth = 10,
        CancellationToken cancel = default) =>
        Call("assembly_tree", new { session, depth }, cancel);

    [McpServerTool(Name = "get_selection", ReadOnly = true)]
    [Description("What the user has selected in a session: faces and edges (with their list_topology index in the part, " +
                 "and in an assembly the occurrence path), " +
                 "occurrences, features, sketches, sketch figures, and design planes, axes and points.")]
    public Task<string> Selection([Description(SessionHelp)] string? session = null, CancellationToken cancel = default) =>
        Call("selection", new { session }, cancel);

    [McpServerTool(Name = "create_part")]
    [Description("Create a new, empty part in its own window. It isn't saved until save_session (with a folder for its first save).")]
    public Task<string> CreatePart(
        [Description("Name of the new part.")] string name,
        [Description("Make a sheet metal part.")] bool sheetMetal = false,
        CancellationToken cancel = default) =>
        Call("create_part", new { name, sheetMetal }, cancel);
}
