using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using AlibreX;

namespace MCPForAlibre.AddOn
{
    // Sketches: creating, adding and listing figures, constraints, dimensions and checking.
    sealed partial class Commands
    {
        void RegisterSketches()
        {
            m_methods["create_sketch"] = CreateSketch;
            m_methods["add_sketch_figures"] = AddSketchFigures;
            m_methods["list_sketch_figures"] = ListSketchFigures;
            m_methods["delete_sketch_figures"] = DeleteSketchFigures;
            m_methods["add_sketch_constraint"] = AddSketchConstraint;
            m_methods["add_sketch_dimension"] = AddSketchDimension;
            m_methods["analyze_sketch"] = AnalyzeSketch;
        }

        object CreateSketch(Args args)
        {
            IADPartSession part = FindPart(args);
            object on;
            if (args.Has("face"))
                on = FaceAt(part, args.Int("face", -1));
            else
            {
                on = ResolveReference(part, args.String("plane"));
                if (!(on is IADDesignPlane) && !(on is IADFace))
                    throw new ArgumentException("'" + args.String("plane") + "' isn't a plane or a face.");
            }
            IADSketch sketch = part.Sketches.AddSketch(null, on, args.OptionalString("name") ?? "");
            return new Dictionary<string, object> { ["name"] = sketch.Name };
        }

        object AddSketchFigures(Args args)
        {
            IADPartSession part = FindPart(args);
            double u = 1.0 / MmPerUnit(part);
            IADSketch sketch = FindSketch(part, args.String("sketch"));
            var figures = args.List("figures");
            var added = new List<object>();
            sketch.BeginChange();
            try
            {
                IADSketchFigures fs = sketch.Figures;
                foreach (Args f in figures)
                {
                    var made = new List<IADSketchFigure>();
                    string type = f.String("type");
                    switch (type)
                    {
                        case "line":
                            made.Add(fs.AddLine(f.Number("x1") * u, f.Number("y1") * u, f.Number("x2") * u, f.Number("y2") * u));
                            break;
                        case "circle":
                            made.Add(fs.AddCircle(f.Number("cx") * u, f.Number("cy") * u, f.Number("radius") * u));
                            break;
                        case "rectangle":
                            foreach (object line in ItemsOf(fs.AddRectangle(
                                         Math.Min(f.Number("x1"), f.Number("x2")) * u, Math.Min(f.Number("y1"), f.Number("y2")) * u,
                                         Math.Max(f.Number("x1"), f.Number("x2")) * u, Math.Max(f.Number("y1"), f.Number("y2")) * u)))
                                made.Add((IADSketchFigure)line);
                            break;
                        case "arc":
                            made.Add(fs.AddCircularArcByCenterStartEnd(f.Number("cx") * u, f.Number("cy") * u,
                                                                       f.Number("x1") * u, f.Number("y1") * u,
                                                                       f.Number("x2") * u, f.Number("y2") * u));
                            break;
                        case "arc3":
                        {
                            // AddCircularArcBy3Points really takes centre, start, end (proven), so find the circle
                            // through the three points here, and go anticlockwise from whichever end reaches the
                            // middle point first.
                            double ax = f.Number("x1"), ay = f.Number("y1"), mx = f.Number("xm"), my = f.Number("ym");
                            double bx = f.Number("x2"), by = f.Number("y2");
                            double d = 2 * (ax * (my - by) + mx * (by - ay) + bx * (ay - my));
                            if (Math.Abs(d) < 1e-12)
                                throw new ArgumentException("The three arc points are in a line.");
                            double a2 = ax * ax + ay * ay, m2 = mx * mx + my * my, b2 = bx * bx + by * by;
                            double cx = (a2 * (my - by) + m2 * (by - ay) + b2 * (ay - my)) / d;
                            double cy = (a2 * (bx - mx) + m2 * (ax - bx) + b2 * (mx - ax)) / d;
                            if (d < 0) // start, middle, end turn clockwise: go anticlockwise from the end instead
                            {
                                (ax, bx) = (bx, ax);
                                (ay, by) = (by, ay);
                            }
                            made.Add(fs.AddCircularArcByCenterStartEnd(cx * u, cy * u, ax * u, ay * u, bx * u, by * u));
                            break;
                        }
                        case "point":
                            made.Add(fs.AddSketchPoint(f.Number("x1") * u, f.Number("y1") * u));
                            break;
                        case "ellipse":
                            made.Add(fs.AddEllipse(f.Number("cx") * u, f.Number("cy") * u, f.Number("majorAxis") * u,
                                                   f.Number("ratio"), Angle(f.OptionalNumber("angle") ?? 0)));
                            break;
                        case "ellipticArc":
                            made.Add(fs.AddEllipticArc(f.Number("cx") * u, f.Number("cy") * u, f.Number("majorAxis") * u, f.Number("ratio"),
                                                       f.Number("x1") * u, f.Number("y1") * u, f.Number("x2") * u, f.Number("y2") * u,
                                                       Angle(f.OptionalNumber("angle") ?? 0)));
                            break;
                        case "spline":
                        {
                            var coords = new List<double>();
                            foreach (object pt in f.Values("points"))
                            {
                                var xy = new List<double>();
                                foreach (object c in (IEnumerable)pt)
                                    xy.Add(Convert.ToDouble(c, CultureInfo.InvariantCulture) * u);
                                if (xy.Count != 2)
                                    throw new ArgumentException("Each spline point must be [x, y].");
                                coords.AddRange(xy);
                            }
                            if (coords.Count < 4)
                                throw new ArgumentException("A spline needs at least two points.");
                            Array points = coords.ToArray();
                            made.Add(fs.AddBsplineByInterpolation(ref points));
                            break;
                        }
                        default:
                            throw new ArgumentException("Unknown figure type '" + type +
                                                        "' (line, circle, rectangle, arc, arc3, point, ellipse, ellipticArc, spline).");
                    }
                    bool reference = f.Bool("reference", false);
                    var ids = new List<object>();
                    foreach (IADSketchFigure m in made)
                    {
                        if (reference)
                            m.IsReference = true; // construction figures have no ID in Alibre
                        else
                            ids.Add(m.ID);
                    }
                    added.Add(new Dictionary<string, object> { ["type"] = type, ["ids"] = ids });
                }
            }
            finally
            {
                sketch.EndChange();
            }
            return new Dictionary<string, object>
            {
                ["sketch"] = sketch.Name,
                ["added"] = added,
                ["figures"] = sketch.Figures.Count,
                ["closed"] = sketch.IsClosed,
            };
        }

        object ListSketchFigures(Args args)
        {
            IADPartSession part = FindPart(args);
            double mm = MmPerUnit(part);
            IADSketch sketch = FindSketch(part, args.String("sketch"));
            var list = new List<object>();
            foreach (IADSketchFigure f in sketch.Figures)
                list.Add(FigureInfo(f, mm));
            return new Dictionary<string, object>
            {
                ["sketch"] = sketch.Name,
                ["closed"] = sketch.IsClosed,
                ["figures"] = list,
                ["constraints"] = sketch.SketchConstraints.Count,
                ["dimensions"] = sketch.Dimensions.Count,
            };
        }

        static Dictionary<string, object> SketchPointInfo(IADSketchPoint p, double mm) => p == null ? null : new Dictionary<string, object>
        {
            ["id"] = FigureId(p),
            ["x"] = Round(p.X * mm),
            ["y"] = Round(p.Y * mm),
        };

        static Dictionary<string, object> FigureInfo(IADSketchFigure f, double mm)
        {
            var info = new Dictionary<string, object>
            {
                ["id"] = FigureId(f),
                ["type"] = EnumName(f.FigureType),
                ["reference"] = f.IsReference,
            };
            try
            {
                switch (f)
                {
                    case IADSketchLine l:
                        info["start"] = SketchPointInfo(l.Start, mm);
                        info["end"] = SketchPointInfo(l.End, mm);
                        info["length"] = Round(l.Length * mm);
                        break;
                    case IADSketchCircularArc a:
                        info["centre"] = SketchPointInfo(a.Center, mm);
                        info["radius"] = Round(a.Radius * mm);
                        info["start"] = SketchPointInfo(a.Start, mm);
                        info["end"] = SketchPointInfo(a.End, mm);
                        info["includedAngle"] = Degrees(a.IncludedAngle); // the API gives radians
                        break;
                    case IADSketchCircle c:
                        info["centre"] = SketchPointInfo(c.Center, mm);
                        info["radius"] = Round(c.Radius * mm);
                        break;
                    case IADSketchEllipticArc e:
                        info["centre"] = SketchPointInfo(e.Center, mm);
                        info["majorAxis"] = Round(e.MajorAxis * mm);
                        info["ratio"] = e.MinorMajorRatio;
                        info["angle"] = Degrees(e.MajorAxisAngle);
                        info["start"] = SketchPointInfo(e.Start, mm);
                        info["end"] = SketchPointInfo(e.End, mm);
                        break;
                    case IADSketchEllipse e:
                        info["centre"] = SketchPointInfo(e.Center, mm);
                        info["majorAxis"] = Round(e.MajorAxis * mm);
                        info["ratio"] = e.MinorMajorRatio;
                        info["angle"] = Degrees(e.MajorAxisAngle);
                        break;
                    case IADSketchBspline b:
                        b.GetDefinition(out int order, out int controlPoints, out int knots, out bool rational, out bool closed, out bool periodic);
                        info["order"] = order;
                        info["controlPoints"] = controlPoints;
                        info["closed"] = closed;
                        info["start"] = new[] { Round(b.StartPoint.X * mm), Round(b.StartPoint.Y * mm) };
                        info["end"] = new[] { Round(b.EndPoint.X * mm), Round(b.EndPoint.Y * mm) };
                        break;
                    case IADSketchPoint p:
                        info["x"] = Round(p.X * mm);
                        info["y"] = Round(p.Y * mm);
                        break;
                }
            }
            catch (COMException ex)
            {
                info["error"] = Describe(ex);
            }
            return info;
        }

        // Runs work with the sketch open for editing, as constraints, dimensions and Analyze need
        // ("Invalid Sketch Session" / "No Active Sketch Session" otherwise).
        static void Editing(IADSketch sketch, Action work)
        {
            sketch.BeginChange();
            try { work(); }
            finally { sketch.EndChange(); }
        }

        // A figure's ID, or null for construction (reference) figures, which Alibre gives none.
        static string FigureId(IADSketchFigure f)
        {
            try { return f.ID; } catch (COMException) { return null; }
        }

        object DeleteSketchFigures(Args args)
        {
            IADPartSession part = FindPart(args);
            IADSketch sketch = FindSketch(part, args.String("sketch"));
            var ids = args.Strings("ids");
            var figures = ids.ConvertAll(id => FindFigure(sketch, id)); // all found before any is deleted
            sketch.BeginChange();
            try
            {
                foreach (var f in figures)
                    f.Delete();
            }
            finally
            {
                sketch.EndChange();
            }
            return new Dictionary<string, object> { ["deleted"] = ids.Count, ["figures"] = sketch.Figures.Count };
        }

        object AddSketchConstraint(Args args)
        {
            IADPartSession part = FindPart(args);
            IADSketch sketch = FindSketch(part, args.String("sketch"));
            string type = args.String("type");
            if (!Enum.TryParse("AD_CONSTRAINT_" + type.ToUpperInvariant(), out ADSketchConstraintType kind) ||
                kind == ADSketchConstraintType.AD_CONSTRAINT_UNKNOWN)
                throw new ArgumentException("Unknown constraint '" + type + "' (horizontal, vertical, coincident, parallel, " +
                                            "perpendicular, tangent, equal, midpoint, collinear, coradial, symmetric, fix, normal, intersection).");
            var ids = args.Strings("ids");
            if (ids.Count == 0)
                throw new ArgumentException("'ids' needs the figures to constrain.");
            var targets = ids.ConvertAll(id => SketchTarget(part, sketch, id));
            // The tool takes symmetric as "these two about that"; Alibre wants the line of symmetry first
            // (given last, it mirrored the line and the axis about the first figure). Proven 2026-11-10.
            if (kind == ADSketchConstraintType.AD_CONSTRAINT_SYMMETRIC && targets.Count == 3)
            {
                object about = targets[2];
                targets.RemoveAt(2);
                targets.Insert(0, about);
            }
            string how = "as given";
            Editing(sketch, () =>
            {
                try
                {
                    sketch.SketchConstraints.AddConstraint(Collect(targets), kind);
                }
                catch (COMException ex) when (targets.Exists(IsDesignGeometry))
                {
                    // Alibre may not take design geometry in a sketch constraint: use it brought into the sketch.
                    Log.Write("sketch", type + " with design geometry refused (" + Describe(ex) + "); using it brought into the sketch");
                    sketch.SketchConstraints.AddConstraint(Collect(targets.ConvertAll(t => InSketch(part, sketch, t))), kind);
                    how = "with design geometry brought into the sketch";
                }
            });
            if (how == "as given" && targets.Exists(IsDesignGeometry))
                Log.Write("sketch", type + " took design geometry directly");
            return new Dictionary<string, object> { ["constraints"] = sketch.SketchConstraints.Count, ["designGeometry"] = how };
        }

        object AddSketchDimension(Args args)
        {
            IADPartSession part = FindPart(args);
            double u = 1.0 / MmPerUnit(part);
            IADSketch sketch = FindSketch(part, args.String("sketch"));
            var ids = args.Strings("ids");
            var targets = ids.ConvertAll(id => SketchTarget(part, sketch, id));
            var figures = new List<IADSketchFigure>(); // filled inside the edit, where design geometry can be brought in
            string kind = args.OptionalString("kind") ?? "linear";
            // With an equation (such as "Width" or "Height / 2"), the dimension is placed at the figure's
            // present size, then its parameter is given the equation, which drives the geometry.
            string equation = args.OptionalString("equation");
            object Size(double current) => equation != null ? current : args.Number("value") * u;
            IADDimension dimension = null;
            Editing(sketch, () =>
            {
            foreach (object t in targets)
                figures.Add(InSketch(part, sketch, t) as IADSketchFigure ??
                            throw new ArgumentException("A dimension needs sketch figures or points."));
            switch (kind)
            {
                case "linear":
                    if (figures.Count == 1 && figures[0] is IADSketchLine line)
                        dimension = sketch.Dimensions.PlaceLinearDimension(line, Size(line.Length));
                    else if (figures.Count == 2 && figures[0] is IADSketchPoint p1 && figures[1] is IADSketchPoint p2)
                        dimension = sketch.Dimensions.PlaceLinearDimension(p1, p2,
                            Size(Math.Sqrt((p1.X - p2.X) * (p1.X - p2.X) + (p1.Y - p2.Y) * (p1.Y - p2.Y))));
                    else
                        throw new ArgumentException("A linear dimension takes one line, or two points.");
                    break;
                case "radial":
                    if (figures.Count != 1)
                        throw new ArgumentException("A radial dimension takes one circle or arc.");
                    double radius = figures[0] is IADSketchCircularArc arc ? arc.Radius
                                  : figures[0] is IADSketchCircle c ? c.Radius
                                  : throw new ArgumentException("A radial dimension takes a circle or an arc.");
                    dimension = sketch.Dimensions.PlaceRadialDimension(figures[0], Size(radius));
                    break;
                case "diametric":
                    if (figures.Count != 1 || !(figures[0] is IADSketchCircle circle))
                        throw new ArgumentException("A diametric dimension takes one circle.");
                    dimension = sketch.Dimensions.PlaceDiametricDimension(circle, Size(2 * circle.Radius));
                    break;
                default:
                    throw new ArgumentException("Unknown dimension kind '" + kind + "' (linear, radial, diametric).");
            }
            });

            var result = new Dictionary<string, object> { ["dimensions"] = sketch.Dimensions.Count };
            IADParameter parameter = null;
            try { parameter = dimension?.Parameter; } catch (COMException) { }
            if (equation != null)
            {
                if (parameter == null)
                    throw new InvalidOperationException("The dimension was placed, but Alibre gave no parameter to put the equation on.");
                IADParameter driven = parameter;
                ParameterChange((IADSession)part, () => driven.Equation = equation);
                ((IADDesignSession)part).RegenerateDesign(false);
            }
            if (parameter != null)
                result["parameter"] = ParameterInfo(parameter, MmPerUnit(part));
            return result;
        }

        object AnalyzeSketch(Args args)
        {
            IADPartSession part = FindPart(args);
            IADSketch sketch = FindSketch(part, args.String("sketch"));
            double u = 1.0 / MmPerUnit(part);
            bool heal = args.Bool("heal", false);
            IAnalyzedSketchData data = null;
            Editing(sketch, () => data = sketch.Analyze(true, true, true, true, true, heal, (args.OptionalNumber("tolerance") ?? 0.01) * u));
            return new Dictionary<string, object>
            {
                ["closed"] = sketch.IsClosed,
                ["disjointEnds"] = Items(data.DisjointEnds),
                ["openLoops"] = Items(data.OpenLoops),
                ["overlaps"] = Items(data.OverLaps),
                ["intersections"] = Items(data.Intersections),
                ["degenerateFigures"] = Items(data.DegenerateFigures),
                ["healed"] = heal,
            };
        }

        // A collector's contents as figure IDs where they are figures, else a kind name.
        static List<object> Items(IObjectCollector collector)
        {
            var list = new List<object>();
            if (collector == null)
                return list;
            foreach (object item in ItemsOf(collector))
                list.Add(item is IADSketchFigure f ? (object)FigureId(f) : item is IObjectCollector c ? Items(c) : (object)"item");
            return list;
        }
    }
}
