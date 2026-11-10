using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AlibreX;

namespace MCPForAlibre.AddOn
{
    // What a sketch constraint or dimension can name, besides a figure ID:
    //   "3.start", "3.end"         a line's or arc's end point (they have no IDs of their own)
    //   "3.centre" / "3.center"    a circle's, arc's or ellipse's centre
    //   "origin"                   the sketch's own origin point
    //   "X-Axis", "Origin", ...    built-in or added design geometry, by name (part_summary lists it)
    // Design geometry is offered to Alibre as it is first; if Alibre refuses it, it is brought into the
    // sketch as fixed construction geometry along it (a line for an axis, a point for a point) and that
    // is used instead, reused on later calls.
    sealed partial class Commands
    {
        const double kProjectedHalfLengthMm = 100;

        object SketchTarget(IADPartSession part, IADSketch sketch, string reference)
        {
            if (string.IsNullOrEmpty(reference))
                throw new ArgumentException("An empty figure reference.");
            if (string.Equals(reference, "origin", StringComparison.OrdinalIgnoreCase))
                return sketch.OriginPoint;

            int dot = reference.LastIndexOf('.');
            if (dot > 0)
            {
                string id = reference.Substring(0, dot);
                string which = reference.Substring(dot + 1).ToLowerInvariant();
                IADSketchFigure f = FindFigure(sketch, id);
                switch (which)
                {
                    case "start":
                        if (f is IADSketchLine l1) return l1.Start;
                        if (f is IADSketchCircularArc a1) return a1.Start;
                        if (f is IADSketchEllipticArc e1) return e1.Start;
                        break;
                    case "end":
                        if (f is IADSketchLine l2) return l2.End;
                        if (f is IADSketchCircularArc a2) return a2.End;
                        if (f is IADSketchEllipticArc e2) return e2.End;
                        break;
                    case "centre":
                    case "center":
                        if (f is IADSketchCircularArc a3) return a3.Center;
                        if (f is IADSketchCircle c3) return c3.Center;
                        if (f is IADSketchEllipticArc e3) return e3.Center;
                        if (f is IADSketchEllipse e4) return e4.Center;
                        break;
                    default:
                        throw new ArgumentException("'" + reference + "': after the dot use start, end or centre.");
                }
                throw new ArgumentException("Figure " + id + " (" + EnumName(f.FigureType) + ") has no " + which + ".");
            }

            IADSketchFigure figure = null;
            try { figure = sketch.Figures.GetFigureByID(reference); } catch (COMException) { }
            if (figure != null)
                return figure;
            try { return DesignGeometry((IADDesignSession)part, reference); }
            catch (ArgumentException)
            {
                throw new ArgumentException("'" + reference + "' isn't a figure ID in sketch '" + sketch.Name + "', a point such as " +
                    "3.start, 3.end or 3.centre, 'origin', or design geometry such as X-Axis, Y-Axis or Origin.");
            }
        }

        static bool IsDesignGeometry(object o) => o is IADDesignAxis || o is IADDesignPoint || o is IADDesignPlane;

        // Design geometry as sketch geometry: a fixed construction line along an axis, or a fixed
        // construction point at a point, found again if an earlier call made it. Call inside BeginChange.
        object InSketch(IADPartSession part, IADSketch sketch, object target)
        {
            var factory = ((IADSession)part).GeometryFactory;
            double mm = MmPerUnit(part);
            switch (target)
            {
                case IADDesignAxis axis:
                {
                    axis.GetGeometry(out IADPoint at, out IADPoint _);
                    IADVector d = axis.Direction;
                    double reach = kProjectedHalfLengthMm / mm;
                    sketch.MapFromWorldToSketch(factory.CreatePoint(at.X - d.X * reach, at.Y - d.Y * reach, at.Z - d.Z * reach), out double u1, out double v1);
                    sketch.MapFromWorldToSketch(factory.CreatePoint(at.X + d.X * reach, at.Y + d.Y * reach, at.Z + d.Z * reach), out double u2, out double v2);
                    double length = Math.Sqrt((u2 - u1) * (u2 - u1) + (v2 - v1) * (v2 - v1));
                    if (length < reach * 1e-6)
                        throw new ArgumentException("'" + axis.Name + "' points straight out of the sketch's plane, so it has no line in the sketch.");
                    foreach (IADSketchFigure f in sketch.Figures)
                        if (f.IsReference && f is IADSketchLine existing && OnLine(existing.Start, u1, v1, u2, v2) && OnLine(existing.End, u1, v1, u2, v2))
                            return existing;
                    IADSketchLine line = sketch.Figures.AddLine(u1, v1, u2, v2);
                    line.IsReference = true;
                    sketch.SketchConstraints.AddConstraint(Collect(new object[] { line }), ADSketchConstraintType.AD_CONSTRAINT_FIX);
                    Log.Write("sketch", "brought " + axis.Name + " into " + sketch.Name + " as a fixed construction line");
                    return line;
                }
                case IADDesignPoint point:
                {
                    sketch.MapFromWorldToSketch(point.Geometry, out double u, out double v);
                    foreach (IADSketchFigure f in sketch.Figures)
                        if (f.IsReference && f is IADSketchPoint existing && Math.Abs(existing.X - u) < 1e-9 && Math.Abs(existing.Y - v) < 1e-9)
                            return existing;
                    IADSketchPoint p = sketch.Figures.AddSketchPoint(u, v);
                    p.IsReference = true;
                    sketch.SketchConstraints.AddConstraint(Collect(new object[] { p }), ADSketchConstraintType.AD_CONSTRAINT_FIX);
                    Log.Write("sketch", "brought " + point.Name + " into " + sketch.Name + " as a fixed construction point");
                    return p;
                }
                case IADDesignPlane plane:
                    throw new ArgumentException("A design plane can't be used in a sketch constraint; use an axis or a point.");
                default:
                    return target;
            }
        }

        static bool OnLine(IADSketchPoint p, double u1, double v1, double u2, double v2)
        {
            double dx = u2 - u1, dy = v2 - v1;
            double cross = (p.X - u1) * dy - (p.Y - v1) * dx;
            return Math.Abs(cross) / Math.Sqrt(dx * dx + dy * dy) < 1e-7;
        }
    }
}
