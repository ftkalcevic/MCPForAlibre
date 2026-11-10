using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using AlibreX;

namespace MCPForAlibre.AddOn
{
    // The bridge's methods, split by area into Commands.*.cs. Every one runs on Alibre's UI thread
    // (see UiThread). Lengths in and out are millimetres, converted with MmPerUnit; angles are degrees.
    sealed partial class Commands
    {
        readonly IADRoot m_root;
        readonly Dictionary<string, Func<Args, object>> m_methods = new Dictionary<string, Func<Args, object>>();

        public Commands(IADRoot root)
        {
            m_root = root;
            RegisterSessions();
            RegisterFeatures();
            RegisterSketches();
            RegisterGeometry();
            RegisterAssemblies();
        }

        public object Invoke(string method, Args args)
        {
            if (!m_methods.TryGetValue(method, out var handler))
                throw new ArgumentException("Unknown method '" + method + "'.");
            return handler(args);
        }

        public static string Describe(Exception ex)
        {
            if (ex is COMException com)
                return com.Message + " (hr=0x" + com.ErrorCode.ToString("X8") + ")";
            return ex.Message;
        }

        // ---- finding sessions, sketches and features ----

        IADSession FindSession(Args args)
        {
            string name = args.OptionalString("session");
            if (string.IsNullOrEmpty(name))
            {
                IADSession top = null;
                try { top = m_root.TopmostSession; } catch (COMException) { }
                return top ?? throw new InvalidOperationException("No session is open in Alibre.");
            }
            return SessionNamed(name);
        }

        // A session by its identifier, or by its name if only one open session has it. Names aren't
        // unique in Alibre (two unsaved parts can both be "BlockA"), so a shared name is refused.
        IADSession SessionNamed(string nameOrId)
        {
            var matches = new List<IADSession>();
            foreach (IADSession s in m_root.Sessions)
            {
                if (string.Equals(s.Identifier, nameOrId, StringComparison.OrdinalIgnoreCase))
                    return s;
                if (string.Equals(s.Name, nameOrId, StringComparison.OrdinalIgnoreCase))
                    matches.Add(s);
            }
            if (matches.Count == 1)
                return matches[0];
            if (matches.Count > 1)
                throw new ArgumentException(matches.Count + " open sessions are called '" + nameOrId + "'. Give the identifier " +
                    "(alibre_status) of the one you mean: " +
                    string.Join(", ", matches.ConvertAll(s => s.Identifier + (string.IsNullOrEmpty(s.FilePath) ? " (unsaved)" : " (" + s.FilePath + ")"))) + ".");
            throw new ArgumentException("No open session is called '" + nameOrId + "'.");
        }

        IADPartSession FindPart(Args args) =>
            FindSession(args) as IADPartSession ?? throw new InvalidOperationException("That session isn't a part.");

        static IADSketch FindSketch(IADPartSession part, string name)
        {
            var names = new List<string>();
            foreach (IADSketch k in part.Sketches)
            {
                if (string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase))
                    return k;
                names.Add(k.Name);
            }
            throw new ArgumentException("No sketch called '" + name + "'. The part has: " + Join(names) + ".");
        }

        static IADPartFeature FindFeature(IADPartSession part, string name)
        {
            var names = new List<string>();
            foreach (IADPartFeature f in part.Features)
            {
                if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                    return f;
                names.Add(f.Name);
            }
            throw new ArgumentException("No feature called '" + name + "'. The part has: " + Join(names) + ".");
        }

        static IADSketchFigure FindFigure(IADSketch sketch, string id)
        {
            IADSketchFigure figure = null;
            try { figure = sketch.Figures.GetFigureByID(id); } catch (COMException) { }
            return figure ?? throw new ArgumentException("Sketch '" + sketch.Name + "' has no figure with ID '" + id +
                                                         "' (list_sketch_figures gives the IDs).");
        }

        // ---- topology: faces and edges by index, in body then enumeration order ----

        static List<IADFace> AllFaces(IADPartSession part)
        {
            var list = new List<IADFace>();
            foreach (IADBody body in part.Bodies)
                foreach (IADFace face in body.Faces)
                    list.Add(face);
            return list;
        }

        static List<IADEdge> AllEdges(IADPartSession part)
        {
            var list = new List<IADEdge>();
            foreach (IADBody body in part.Bodies)
                foreach (IADEdge edge in body.Edges)
                    list.Add(edge);
            return list;
        }

        static IADFace FaceAt(IADPartSession part, int index)
        {
            var faces = AllFaces(part);
            if (index < 0 || index >= faces.Count)
                throw new ArgumentException("Face " + index + " doesn't exist; the part has " + faces.Count +
                                            " faces (0 to " + (faces.Count - 1) + "). List them again with list_topology.");
            return faces[index];
        }

        static IADEdge EdgeAt(IADPartSession part, int index)
        {
            var edges = AllEdges(part);
            if (index < 0 || index >= edges.Count)
                throw new ArgumentException("Edge " + index + " doesn't exist; the part has " + edges.Count +
                                            " edges (0 to " + (edges.Count - 1) + "). List them again with list_topology.");
            return edges[index];
        }

        // The faces and edges a feature works on: "faces" and "edges" index lists, plus the user's
        // selection in Alibre when "useSelection" is true. Throws if nothing results.
        IObjectCollector ResolveTopology(IADPartSession part, Args args, bool facesAllowed = true, bool edgesAllowed = true)
        {
            IObjectCollector collector = m_root.NewObjectCollector();
            int count = 0;
            if (facesAllowed)
                foreach (int i in args.Ints("faces")) { collector.Add(FaceAt(part, i)); ++count; }
            if (edgesAllowed)
                foreach (int i in args.Ints("edges")) { collector.Add(EdgeAt(part, i)); ++count; }
            if (args.Bool("useSelection", false))
            {
                foreach (object item in ItemsOf(((IADSession)part).SelectedObjects))
                {
                    object target = item is IADTargetProxy proxy ? proxy.Target : item;
                    if ((facesAllowed && target is IADFace) || (edgesAllowed && target is IADEdge))
                    {
                        collector.Add(target);
                        ++count;
                    }
                }
            }
            if (count == 0)
                throw new ArgumentException("Nothing to work on: give " +
                    (facesAllowed && edgesAllowed ? "faces or edges" : facesAllowed ? "faces" : "edges") +
                    " (indexes from list_topology), or useSelection with them selected in Alibre.");
            return collector;
        }

        // A geometry reference by text: a design plane, axis or point name (as part_summary lists them),
        // "face:N" or "edge:N" (list_topology indexes), or "figure:ID" in the given sketch.
        object ResolveReference(IADPartSession part, string reference, IADSketch sketch = null)
        {
            if (string.IsNullOrEmpty(reference))
                throw new ArgumentException("A geometry reference is required.");
            int colon = reference.IndexOf(':');
            if (colon > 0)
            {
                string kind = reference.Substring(0, colon).Trim().ToLowerInvariant();
                string rest = reference.Substring(colon + 1).Trim();
                if (kind == "face" || kind == "edge")
                {
                    if (!int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                        throw new ArgumentException("'" + reference + "' needs a number after the colon.");
                    return kind == "face" ? (object)FaceAt(part, index) : EdgeAt(part, index);
                }
                if (kind == "figure")
                {
                    if (sketch == null)
                        throw new ArgumentException("'" + reference + "' can only be used where a sketch is given.");
                    return FindFigure(sketch, rest);
                }
            }
            var design = (IADDesignSession)part;
            foreach (IADDesignPlane p in design.DesignPlanes)
                if (string.Equals(p.Name, reference, StringComparison.OrdinalIgnoreCase))
                    return p;
            foreach (IADDesignAxis a in design.DesignAxes)
                if (string.Equals(a.Name, reference, StringComparison.OrdinalIgnoreCase))
                    return a;
            foreach (IADDesignPoint p in design.DesignPoints)
                if (string.Equals(p.Name, reference, StringComparison.OrdinalIgnoreCase))
                    return p;
            throw new ArgumentException("Nothing called '" + reference + "'. Use a design plane, axis or point name " +
                                        "(part_summary), face:N or edge:N (list_topology), or figure:ID.");
        }

        // Alibre's collectors don't enumerate in .NET ("Unable to cast ObjectCollector to IEnumerable"),
        // so read them by index. Unproven whether Item is 0- or 1-based: try 0 first, then 1.
        static List<object> ItemsOf(IObjectCollector collector)
        {
            var list = new List<object>();
            if (collector == null)
                return list;
            int count = collector.Count;
            if (count == 0)
                return list;
            int first = 0;
            try { if (collector.Item(0) == null) first = 1; }
            catch (Exception) { first = 1; }
            for (int i = first; i < first + count; ++i)
                list.Add(collector.Item(i));
            return list;
        }

        IObjectCollector Collect(IEnumerable<object> items)
        {
            IObjectCollector collector = m_root.NewObjectCollector();
            foreach (object item in items)
                collector.Add(item);
            return collector;
        }

        // ---- units and names ----

        static double MmPerUnit(IADDesignSession d)
        {
            switch (d.DesignProperties.ModelUnits)
            {
                case ADUnits.AD_MILLIMETERS: return 1.0;
                case ADUnits.AD_CENTIMETERS: return 10.0;
                case ADUnits.AD_METERS: return 1000.0;
                case ADUnits.AD_INCHES: return 25.4;
                case ADUnits.AD_FEET:
                case ADUnits.AD_FEET_INCHES: return 304.8;
                default: return 10.0; // new files are cm
            }
        }

        static double MmPerUnit(IADPartSession part) => MmPerUnit((IADDesignSession)part);

        static double[] Mm(IADPoint p, double mm) =>
            p == null ? null : new[] { Round(p.X * mm), Round(p.Y * mm), Round(p.Z * mm) };

        static double Round(double v) => Math.Round(v, 6);

        static string Kind(ADObjectSubType t) => EnumName(t);

        static string UnitName(ADUnits u) => EnumName(u);

        static string EnumName(Enum e)
        {
            string s = e.ToString();
            return s.StartsWith("AD_") ? s.Substring(3).ToLowerInvariant() : s;
        }

        static string Join(List<string> names) => names.Count == 0 ? "none" : string.Join(", ", names);
    }
}
