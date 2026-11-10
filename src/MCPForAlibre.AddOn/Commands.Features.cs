using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AlibreX;

namespace MCPForAlibre.AddOn
{
    // Topology listing and part features.
    sealed partial class Commands
    {
        // The API takes angles in radians: revolving 360 and 90 "degrees" as given swept 106.5 and
        // 116.6 degrees (360 and 90 radians, wrapped), proven in Alibre on 2026-11-10.
        static double Angle(double degrees) => degrees * Math.PI / 180.0;

        static double Degrees(double radians) => Round(radians * 180.0 / Math.PI);

        void RegisterFeatures()
        {
            m_methods["list_topology"] = ListTopology;
            m_methods["extrude"] = Extrude;
            m_methods["revolve"] = Revolve;
            m_methods["sweep"] = Sweep;
            m_methods["loft"] = Loft;
            m_methods["fillet"] = Fillet;
            m_methods["chamfer"] = Chamfer;
            m_methods["shell"] = Shell;
            m_methods["draft"] = Draft;
            m_methods["hole"] = Hole;
            m_methods["offset_face"] = OffsetFace;
            m_methods["suppress_feature"] = a => SetSuppressed(a, true);
            m_methods["unsuppress_feature"] = a => SetSuppressed(a, false);
            m_methods["delete_feature"] = DeleteFeature;
            m_methods["rename_feature"] = RenameFeature;
        }

        // ---- topology ----

        object ListTopology(Args args)
        {
            IADPartSession part;
            Func<IADPoint, double[]> at;          // a point in the part's units to mm in the answer's frame
            Func<double, double, double, double[]> along = (x, y, z) => new[] { x, y, z };
            if (args.Has("occurrence"))
            {
                IADAssemblySession assembly = FindAssembly(args);
                IADOccurrence occurrence = FindOccurrence(assembly, args.String("occurrence"));
                part = occurrence.DesignSession as IADPartSession ??
                       throw new ArgumentException("'" + args.String("occurrence") + "' isn't a part.");
                double assemblyMm = MmPerUnit((IADDesignSession)assembly);
                double scale = MmPerUnit(part) / assemblyMm; // part units to assembly units
                Placement world = Placement.From(occurrence.WorldTransform);
                at = p =>
                {
                    if (p == null) return null;
                    double[] a = world.Point(p.X * scale, p.Y * scale, p.Z * scale);
                    return new[] { Round(a[0] * assemblyMm), Round(a[1] * assemblyMm), Round(a[2] * assemblyMm) };
                };
                along = world.Direction;
            }
            else
            {
                part = FindPart(args);
                double partMm = MmPerUnit(part);
                at = p => Mm(p, partMm);
            }
            var faces = AllFaces(part);
            var faceIndex = new Dictionary<IntPtr, int>();
            for (int i = 0; i < faces.Count; ++i)
                faceIndex[Identity(faces[i])] = i;

            var faceList = new List<object>();
            for (int i = 0; i < faces.Count; ++i)
            {
                IADFace face = faces[i];
                var info = new Dictionary<string, object> { ["index"] = i };
                try
                {
                    IADSurface surface = face.Geometry;
                    info["surface"] = EnumName(surface.SurfaceType);
                    face.GetExtents(out IADPoint lo, out IADPoint hi);
                    double[] a = at(lo), b = at(hi);
                    // In an assembly the part's box turns with it, so give the box of its corners.
                    info["min"] = new[] { Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]) };
                    info["max"] = new[] { Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2]) };
                    info["centre"] = new[] { Round((a[0] + b[0]) / 2), Round((a[1] + b[1]) / 2), Round((a[2] + b[2]) / 2) };
                    if (surface.SurfaceType == ADGeometryType.AD_PLANE)
                    {
                        surface.GetParameterExtents(out double u0, out double u1, out double v0, out double v1);
                        IADVector n = surface.NormalAtParam((u0 + u1) / 2, (v0 + v1) / 2);
                        double sign = face.IsSenseReversed ? -1 : 1; // unproven: that this makes it point out of the material
                        double[] d = along(sign * n.X, sign * n.Y, sign * n.Z);
                        info["normal"] = new[] { Round(d[0]), Round(d[1]), Round(d[2]) };
                    }
                }
                catch (COMException ex)
                {
                    info["error"] = Describe(ex);
                }
                faceList.Add(info);
            }

            var edges = AllEdges(part);
            var edgeList = new List<object>();
            for (int i = 0; i < edges.Count; ++i)
            {
                IADEdge edge = edges[i];
                var info = new Dictionary<string, object> { ["index"] = i };
                try
                {
                    IADCurve curve = edge.Geometry;
                    info["curve"] = EnumName(curve.CurveType);
                    info["start"] = at(TryPoint(edge.StartVertex));
                    info["end"] = at(TryPoint(edge.EndVertex));
                    curve.GetParameterExtents(out double t0, out double t1);
                    info["middle"] = at(curve.PointAtParam((t0 + t1) / 2));
                    var adjacent = new List<object>();
                    foreach (IADFace f in edge.Faces)
                        if (faceIndex.TryGetValue(Identity(f), out int fi))
                            adjacent.Add(fi);
                    info["faces"] = adjacent;
                }
                catch (COMException ex)
                {
                    info["error"] = Describe(ex);
                }
                edgeList.Add(info);
            }

            return new Dictionary<string, object> { ["faces"] = faceList, ["edges"] = edgeList };
        }

        static IADPoint TryPoint(IADVertex v)
        {
            try { return v?.Point; } catch (COMException) { return null; }
        }

        static IntPtr Identity(object com)
        {
            IntPtr p = Marshal.GetIUnknownForObject(com);
            Marshal.Release(p); // the pointer stays valid while the wrapper is alive; it's only compared
            return p;
        }

        // The given name, or the kind followed by the first unused <n>, as Alibre names features itself.
        static string FeatureName(IADPartSession part, Args args, string kind)
        {
            string name = args.OptionalString("name");
            if (!string.IsNullOrEmpty(name))
                return name;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IADPartFeature f in part.Features)
                used.Add(f.Name);
            for (int n = 1; ; ++n)
                if (!used.Contains(kind + "<" + n + ">"))
                    return kind + "<" + n + ">";
        }

        // A parameter name not yet used in the session, nor in 'alsoTaken' (names picked but not yet
        // created): prefix1, prefix2, ...
        static string UnusedParameterName(IADSession session, string prefix, params string[] alsoTaken)
        {
            var used = new HashSet<string>(alsoTaken, StringComparer.OrdinalIgnoreCase);
            foreach (IADParameter p in session.Parameters)
                used.Add(p.Name);
            for (int n = 1; ; ++n)
                if (!used.Contains(prefix + n))
                    return prefix + n;
        }

        static Dictionary<string, object> FeatureResult(object feature)
        {
            var f = (IADPartFeature)feature;
            return new Dictionary<string, object> { ["name"] = f.Name, ["type"] = EnumName(f.FeatureType), ["hasError"] = f.HasError };
        }

        // ---- sketch-based features ----

        object Extrude(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "Extrusion"); // before any faces are read
            double u = 1.0 / MmPerUnit(part);
            IADSketch sketch = FindSketch(part, args.String("sketch"));

            string end = args.OptionalString("end") ??
                         (args.Bool("throughAll", false) ? "throughAll" : args.Bool("midPlane", false) ? "midPlane" : "depth");
            ADPartFeatureEndCondition condition;
            object to = null;
            double offset = 0;
            switch (end)
            {
                case "depth": condition = ADPartFeatureEndCondition.AD_TO_DEPTH; break;
                case "midPlane": condition = ADPartFeatureEndCondition.AD_MID_PLANE; break;
                case "throughAll": condition = ADPartFeatureEndCondition.AD_THROUGH_ALL; break;
                case "toNext": condition = ADPartFeatureEndCondition.AD_TO_NEXT; break;
                case "toGeometry":
                    condition = ADPartFeatureEndCondition.AD_TO_GEOMETRY;
                    to = ResolveReference(part, args.String("to"));
                    offset = (args.OptionalNumber("offset") ?? 0) * u;
                    break;
                default: throw new ArgumentException("Unknown end '" + end + "' (depth, midPlane, throughAll, toNext, toGeometry).");
            }
            object depth = condition == ADPartFeatureEndCondition.AD_TO_DEPTH || condition == ADPartFeatureEndCondition.AD_MID_PLANE
                ? args.Number("depth") * u : 0.0;
            object draft = Angle(args.OptionalNumber("draftAngle") ?? 0);
            bool outward = args.Bool("outwardDraft", false);
            bool reversed = args.Bool("reverse", false);

            object feature = args.Bool("cut", false)
                ? part.Features.AddExtrudedCutout(sketch, depth, condition, null, to, offset, ADDirectionType.AD_ALONG_NORMAL,
                                                  null, null, reversed, draft, outward, name, "", "")
                : part.Features.AddExtrudedBoss(sketch, depth, condition, null, to, offset, ADDirectionType.AD_ALONG_NORMAL,
                                                null, null, reversed, draft, outward, name, "", "");
            return FeatureResult(feature);
        }

        object Revolve(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "Revolution"); // before any faces are read
            IADSketch sketch = FindSketch(part, args.String("sketch"));
            object axis = ResolveReference(part, args.String("axis"), sketch);
            object angle = Angle(args.OptionalNumber("angle") ?? 360);
            object feature = args.Bool("cut", false)
                ? part.Features.AddRevolvedCutout(sketch, null, axis, angle, name)
                : part.Features.AddRevolvedBoss(sketch, null, axis, angle, name);
            return FeatureResult(feature);
        }

        object Sweep(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "Sweep"); // before any faces are read
            IADSketch profile = FindSketch(part, args.String("sketch"));
            var paths = args.Strings("path");
            if (paths.Count == 0)
                throw new ArgumentException("'path' needs at least one sketch name.");
            var pathSketches = paths.ConvertAll(p => (object)FindSketch(part, p));
            bool rigid = args.Bool("rigid", false);
            bool cut = args.Bool("cut", false);

            object Add(IObjectCollector path) => cut
                ? part.Features.AddSweptCutout(profile, path, rigid, ADPartFeatureEndCondition.AD_ENTIRE_PATH, null, null, 0, 0.0, false, name)
                : part.Features.AddSweptBoss(profile, path, rigid, ADPartFeatureEndCondition.AD_ENTIRE_PATH, null, null, 0, 0.0, false, name);

            // Unproven whether the path collector holds sketches or their figures; try sketches first.
            try
            {
                return FeatureResult(Add(Collect(pathSketches)));
            }
            catch (COMException ex)
            {
                Log.Write("sweep", "path as sketches failed (" + Describe(ex) + "); trying their figures");
                var figures = new List<object>();
                foreach (IADSketch s in pathSketches)
                    foreach (IADSketchFigure f in s.Figures)
                        if (!f.IsReference)
                            figures.Add(f);
                return FeatureResult(Add(Collect(figures)));
            }
        }

        object Loft(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "Loft"); // before any faces are read
            var names = args.Strings("sketches");
            if (names.Count < 2)
                throw new ArgumentException("'sketches' needs at least two sketch names, in order.");
            var sections = Collect(names.ConvertAll(n => (object)FindSketch(part, n)));
            IObjectCollector empty() => m_root.NewObjectCollector();
            bool twist = args.Bool("minimizeTwist", true);
            bool connect = args.Bool("connectEnds", false);
            object feature = args.Bool("cut", false)
                ? part.Features.AddLoftCut(sections, empty(), empty(), empty(), empty(), ADLoftGuideType.AD_NONE, twist, false, false, connect, name)
                : part.Features.AddLoftBoss(sections, empty(), empty(), empty(), empty(), ADLoftGuideType.AD_NONE, twist, false, false, connect, name);
            return FeatureResult(feature);
        }

        // ---- features on faces and edges ----

        object Fillet(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "Fillet"); // before any faces are read
            double u = 1.0 / MmPerUnit(part);
            var targets = ResolveTopology(part, args);
            object feature = part.Features.AddConstantRadiusFilletFeature(targets, args.Number("radius") * u,
                args.Bool("tangentPropagate", true), "", name);
            return FeatureResult(feature);
        }

        object Chamfer(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "Chamfer"); // before any faces are read
            double u = 1.0 / MmPerUnit(part);
            var targets = ResolveTopology(part, args);
            string kind = args.OptionalString("kind") ?? "equal";
            double d1 = args.Number("distance") * u;
            double d2 = d1;
            double angle = 45;
            ADEdgeChamferType type;
            switch (kind)
            {
                case "equal": type = ADEdgeChamferType.AD_EQUAL_DISTANCE; break;
                case "twoDistances": type = ADEdgeChamferType.AD_DISTANCE_TO_DISTANCE; d2 = args.Number("distance2") * u; break;
                case "angleDistance": type = ADEdgeChamferType.AD_ANGLE_DISTANCE; angle = args.Number("angle"); break;
                default: throw new ArgumentException("Unknown chamfer kind '" + kind + "' (equal, twoDistances, angleDistance).");
            }
            object feature = part.Features.AddEdgeChamferFeature(targets, type, d1, d2, Angle(angle),
                args.Bool("tangentPropagate", true), "", "", "", name);
            return FeatureResult(feature);
        }

        object Shell(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "Shell"); // before any faces are read
            double u = 1.0 / MmPerUnit(part);
            if (!args.Bool("outward", false))
                throw new ArgumentException("Alibre 27.0.1's API makes only outward shells (every inward form is refused " +
                                            "with VALUE_NEGATIVE). Use outward: true, which adds the wall outside the part.");
            IObjectCollector remove = args.Values("faces").Count > 0 || args.Bool("useSelection", false)
                ? ResolveTopology(part, args, edgesAllowed: false)
                : m_root.NewObjectCollector(); // no faces removed: a closed hollow
            double thickness = args.Number("thickness") * u;

            // Proven on 2026-11-10: the per-face thickness arguments can't be empty (an empty collector and
            // array gives "Array sizes not matching"; null gives "Object no longer exists" or "Attempt to
            // access index 0"). Giving one face that isn't removed the standard thickness is accepted, and
            // makes an outward shell whatever outward says (a negative thickness too).
            object keep = null;
            var removed = ItemsOf(remove).ConvertAll(Identity);
            foreach (IADFace face in AllFaces(part))
                if (!removed.Contains(Identity(face))) { keep = face; break; }
            if (keep == null)
                throw new ArgumentException("Every face would be removed.");
            Array sizes = new double[] { thickness };
            try
            {
                object feature = part.Features.AddShellFeature(remove, thickness, true, Collect(new[] { keep }), ref sizes, "", name);
                return FeatureResult(feature);
            }
            catch (COMException)
            {
                DeleteIfLeft(part, name);
                throw;
            }
        }

        object Draft(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "Draft"); // before any faces are read
            var faces = ResolveTopology(part, args, edgesAllowed: false);
            object neutral = ResolveReference(part, args.String("neutralPlane"));
            object feature = part.Features.AddDraftFeature(faces, null, neutral, Angle(args.Number("angle")),
                args.Bool("outward", false), "", name);
            return FeatureResult(feature);
        }

        object Hole(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "Hole"); // before any faces are read
            double u = 1.0 / MmPerUnit(part);
            IADSketch sketch = FindSketch(part, args.String("sketch"));
            object on = null;
            try { on = sketch.SketchPlane?.Target; } catch (COMException) { }
            if (on is IADDesignPlane)
                throw new ArgumentException("Alibre places holes only from a sketch on a face. Make the sketch with " +
                                            "create_sketch face (list_topology gives the face's index).");
            bool through = args.Bool("throughAll", false);
            var condition = through ? ADHoleDepthCondition.AD_HOLE_THROUGH_ALL : ADHoleDepthCondition.AD_HOLE_TO_DEPTH;
            object depth = through ? 0.0 : args.Number("depth") * u;
            object diameter = args.Number("diameter") * u;
            bool reversed = args.Bool("reverse", false);
            var f = part.Features;
            object feature;
            switch (args.OptionalString("kind") ?? "simple")
            {
                case "simple":
                    feature = f.AddSimpleHoleEx(sketch, depth, diameter, reversed, null, condition, null, null, 0, name, "", "");
                    break;
                case "drilled":
                    feature = f.AddDrilledHoleEx(sketch, depth, diameter, Angle(args.OptionalNumber("drillAngle") ?? 118),
                        reversed, null, condition, null, null, 0, name, "", "", "");
                    break;
                case "counterbore":
                {
                    object cbDepth = args.Number("counterboreDepth") * u, cbDia = args.Number("counterboreDiameter") * u;
                    object deep = 1000.0 / MmPerUnit(part); // through all as a depth, for the last tries
                    feature = WithFallback(part, name, "counterbore",
                        ("Ex", () => f.AddCounterBoredHoleEx(sketch, depth, diameter, cbDepth, cbDia, reversed, null, condition, null, null, 0, name, "", "", "", "")),
                        ("older", () => f.AddCounterBoredHole(sketch, depth, diameter, cbDepth, cbDia, reversed, null, condition, null, null, 0, name, "")),
                        ("Ex to a depth", () => f.AddCounterBoredHoleEx(sketch, through ? deep : depth, diameter, cbDepth, cbDia, reversed, null,
                                                                        ADHoleDepthCondition.AD_HOLE_TO_DEPTH, null, null, 0, name, "", "", "", "")),
                        ("counterdrill at 180 degrees", () => f.AddCounterDrilledHoleEx(sketch, depth, diameter, cbDepth, cbDia, Angle(180), reversed,
                                                                        null, condition, null, null, 0, name, "", "", "", "", "")));
                    break;
                }
                case "countersink":
                {
                    object csDia = args.Number("countersinkDiameter") * u, csAngle = Angle(args.OptionalNumber("countersinkAngle") ?? 90);
                    feature = WithFallback(part, name, "countersink",
                        ("Ex", () => f.AddCounterSunkHoleEx(sketch, depth, diameter, csDia, csAngle, reversed, null, condition, null, null, 0, name, "", "", "", "")),
                        ("older", () => f.AddCounterSunkHole(sketch, depth, diameter, csDia, csAngle, reversed, null, condition, null, null, 0, name, "")));
                    break;
                }
                default:
                    throw new ArgumentException("Unknown hole kind '" + args.OptionalString("kind") + "' (simple, drilled, counterbore, countersink).");
            }
            return FeatureResult(feature);
        }

        // Tries each way of making a feature in turn, logging which Alibre accepted.
        // A refused feature can still be left in the part with an error; remove it before the next try.
        static void DeleteIfLeft(IADPartSession part, string name)
        {
            foreach (IADPartFeature f in part.Features)
                if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    try { f.Delete(); Log.Write("feature", "deleted the refused " + name); }
                    catch (COMException ex) { Log.Write("feature", "couldn't delete the refused " + name + ": " + Describe(ex)); }
                    return;
                }
        }

        static object WithFallback(IADPartSession part, string name, string what, params (string label, Func<object> make)[] tries)
        {
            COMException last = null;
            foreach (var t in tries)
            {
                try
                {
                    object result = t.make();
                    Log.Write("feature", what + ": " + t.label + " accepted");
                    return result;
                }
                catch (COMException ex)
                {
                    Log.Write("feature", what + ": " + t.label + " refused: " + Describe(ex));
                    DeleteIfLeft(part, name);
                    last = ex;
                }
            }
            throw last;
        }

        object OffsetFace(Args args)
        {
            IADPartSession part = FindPart(args);
            string name = FeatureName(part, args, "OffsetFace"); // before any faces are read
            double u = 1.0 / MmPerUnit(part);
            var faces = ResolveTopology(part, args, edgesAllowed: false);
            object feature = part.Features.AddOffsetFaceFeature(faces, args.Number("offset") * u, name,
                                                                  UnusedParameterName((IADSession)part, "Offset"));
            return FeatureResult(feature);
        }

        // ---- managing features ----

        object SetSuppressed(Args args, bool suppressed)
        {
            IADPartFeature f = FindFeature(FindPart(args), args.String("feature"));
            f.IsSuppressed = suppressed;
            return new Dictionary<string, object> { ["name"] = f.Name, ["suppressed"] = f.IsSuppressed, ["hasError"] = f.HasError };
        }

        object DeleteFeature(Args args)
        {
            IADPartFeature f = FindFeature(FindPart(args), args.String("feature"));
            string name = f.Name;
            f.Delete();
            return new Dictionary<string, object> { ["deleted"] = name };
        }

        object RenameFeature(Args args)
        {
            IADPartFeature f = FindFeature(FindPart(args), args.String("feature"));
            f.Name = args.String("newName");
            return new Dictionary<string, object> { ["name"] = f.Name };
        }
    }
}
