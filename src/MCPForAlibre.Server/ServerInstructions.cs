namespace MCPForAlibre.Server;

// Sent to every client when it connects (MCP's server instructions): the conventions and the
// limits of Alibre Design 27.0.1's API that span several tools. Keep it short; each tool's own
// description covers its details. Update it with any new limit found in Alibre.
static class ServerInstructions
{
    public const string Text = """
        Drives Alibre Design 27.0.1 through an add-on inside it.

        Conventions
        - Lengths are millimetres and angles degrees, in and out, whatever the document's units.
        - Sessions are named by the name on Alibre's window tab, or by the identifier from alibre_status. Names can repeat (two unsaved parts can both be "Part1"); then use the identifier.
        - Faces and edges are numbered by list_topology. Any feature can renumber them, so list again before using an index after a change. In an assembly, give list_topology an occurrence.
        - Design geometry (XY-Plane, YZ-Plane, ZX-Plane, X-Axis, Y-Axis, Z-Axis, Origin, and any added) is named as part_summary lists it.
        - In sketch constraints and dimensions, figures are named by ID, line and arc points as "ID.start", "ID.end" and "ID.centre", the sketch origin as "origin", and design axes and points by name.
        - Parameters (create_parameter, set_parameter) and sketch dimensions take Alibre equations such as "10 mm", "Width * 2". A bare number is in the document's display units.
        - Occurrences in an assembly are named by their path from assembly_tree, such as "Bracket<1>" or "Sub<1>/Pin<2>".

        What Alibre 27.0.1's API can't do, and what to do instead
        - No pattern or mirror features: repeat the feature, or draw the copies in the sketch (mirroring sketch geometry with symmetric constraints).
        - Shells are outward only: the wall is added outside the part. For an inward shell, make the part smaller by the wall first, or cut the inside with an extrude.
        - Holes need their sketch on a face (create_sketch face), not on a design plane. Put hole centres in that sketch as points.
        - Sketch constraints can't use design axes or points directly: constraining to X-Axis or Origin adds a fixed construction line or point to the sketch (it lists with reference true and no ID), which is expected. The sketch's own "origin" is used directly.
        - A counterbore "through all" is made 1000 mm deep.
        - Construction figures and line end points have no IDs; refer to a line's ends as "ID.start" and "ID.end".
        - An assembly can only insert parts and assemblies that have been saved: save a new part first with save_session and a folder.
        - Moving an occurrence that constraints hold may not take effect.

        Timing
        - If Alibre isn't running, the first call starts it and waits for it to load, which can take 15 to 60 seconds. If a call times out meanwhile, call again.
        """;
}
