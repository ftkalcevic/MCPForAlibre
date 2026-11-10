using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AlibreX;

namespace MCPForAlibre.AddOn
{
    // Assemblies: occurrences, their placement, constraints and interference.
    // An occurrence is named by its path from the top: child names joined by "/", such as
    // "Bracket<1>" or "Sub<1>/Pin<2>"; a name that's unique anywhere in the tree also works.
    sealed partial class Commands
    {
        void RegisterAssemblies()
        {
            m_methods["create_assembly"] = CreateAssembly;
            m_methods["add_occurrence"] = AddOccurrence;
            m_methods["add_empty_occurrence"] = AddEmptyOccurrence;
            m_methods["occurrence_info"] = OccurrenceInfoMethod;
            m_methods["move_occurrence"] = MoveOccurrence;
            m_methods["set_occurrence"] = SetOccurrence;
            m_methods["add_assembly_constraint"] = AddAssemblyConstraint;
            m_methods["list_assembly_constraints"] = ListAssemblyConstraints;
            m_methods["suppress_assembly_constraint"] = SuppressAssemblyConstraint;
            m_methods["delete_assembly_constraint"] = DeleteAssemblyConstraint;
            m_methods["check_interference"] = CheckInterference;
        }

        // ---- finding ----

        IADAssemblySession FindAssembly(Args args) =>
            FindSession(args) as IADAssemblySession ?? throw new InvalidOperationException("That session isn't an assembly.");

        static string PathOf(IADOccurrence o)
        {
            var names = new List<string>();
            for (IADOccurrence cur = o; cur != null && cur.NestLevel > 0; cur = cur.ParentOccurrence)
                names.Insert(0, cur.Name);
            return string.Join("/", names);
        }

        static void Walk(IADOccurrence o, List<IADOccurrence> into)
        {
            foreach (IADOccurrence child in o.Occurrences)
            {
                into.Add(child);
                Walk(child, into);
            }
        }

        static IADOccurrence FindOccurrence(IADAssemblySession assembly, string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("An occurrence is required (its path from assembly_tree, such as Part<1>).");
            var all = new List<IADOccurrence>();
            Walk(assembly.RootOccurrence, all);
            foreach (var o in all)
                if (string.Equals(PathOf(o), path, StringComparison.OrdinalIgnoreCase))
                    return o;
            var byName = all.FindAll(o => string.Equals(o.Name, path, StringComparison.OrdinalIgnoreCase));
            if (byName.Count == 1)
                return byName[0];
            if (byName.Count > 1)
                throw new ArgumentException("More than one occurrence is called '" + path + "'; give its path: " +
                                            string.Join(", ", byName.ConvertAll(PathOf)) + ".");
            throw new ArgumentException("No occurrence '" + path + "'. The assembly has: " +
                                        Join(all.ConvertAll(PathOf)) + ".");
        }

        // ---- creating and adding ----

        object CreateAssembly(Args args)
        {
            IADAssemblySession assembly = m_root.CreateEmptyAssemblyEx(args.String("name"), true);
            return SessionInfo((IADSession)assembly);
        }

        // The pose given in mm and degrees, as a transform in the assembly's units.
        Placement PoseFrom(Args args, double assemblyMm, Placement fallback = null)
        {
            double u = 1.0 / assemblyMm;
            if (fallback != null && !Array.Exists(new[] { "x", "y", "z", "rx", "ry", "rz" }, args.Has))
                return fallback;
            double[] t = fallback?.Translation ?? new double[3];
            double[] r = fallback?.RotationDegrees() ?? new double[3];
            return Placement.FromPose(
                args.OptionalNumber("x") * u ?? t[0], args.OptionalNumber("y") * u ?? t[1], args.OptionalNumber("z") * u ?? t[2],
                args.OptionalNumber("rx") ?? r[0], args.OptionalNumber("ry") ?? r[1], args.OptionalNumber("rz") ?? r[2]);
        }

        object AddOccurrence(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            var design = (IADDesignSession)assembly;
            object what;
            if (args.Has("file"))
                what = args.String("file");
            else if (args.Has("design"))
            {
                string name = args.String("design");
                IADSession found = SessionNamed(name);
                what = found as IADDesignSession ??
                       throw new ArgumentException("'" + name + "' isn't a part or an assembly.");
                if (string.IsNullOrEmpty(found.FilePath))
                    throw new InvalidOperationException("'" + name + "' has never been saved, and Alibre only inserts saved " +
                                                        "designs (\"Can't insert Occurrence\"). Save it first with save_session folder.");
            }
            else
                throw new ArgumentException("Give a file path, or the name of an open part or assembly as design.");

            var placement = PoseFrom(args, MmPerUnit(design)).ToTransform(((IADSession)assembly).GeometryFactory);
            IADOccurrence occurrence = assembly.RootOccurrence.Occurrences.Add(ref what, placement);
            return OccurrenceInfo(occurrence, MmPerUnit(design));
        }

        object AddEmptyOccurrence(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            var design = (IADDesignSession)assembly;
            var placement = PoseFrom(args, MmPerUnit(design)).ToTransform(((IADSession)assembly).GeometryFactory);
            string name = args.String("name");
            IADOccurrence occurrence = (args.OptionalString("kind") ?? "part") == "assembly"
                ? assembly.RootOccurrence.Occurrences.AddEmptyAssembly(name, placement)
                : assembly.RootOccurrence.Occurrences.AddEmptyPart(name, args.Bool("sheetMetal", false), placement);
            return OccurrenceInfo(occurrence, MmPerUnit(design));
        }

        // ---- reading and changing ----

        static Dictionary<string, object> OccurrenceInfo(IADOccurrence o, double mm)
        {
            var info = new Dictionary<string, object>
            {
                ["name"] = o.Name,
                ["path"] = PathOf(o),
                ["hidden"] = o.IsHidden,
                ["suppressed"] = o.IsSuppressed,
                ["anchored"] = o.IsAnchored,
            };
            try
            {
                IADDesignSession d = o.DesignSession;
                info["kind"] = Kind(((IADSession)d).SessionType);
                info["filePath"] = ((IADSession)d).FilePath;
                info["design"] = ((IADSession)d).Name;
            }
            catch (COMException) { }
            try
            {
                var local = Placement.From(o.LocalTransform);
                var t = local.Translation;
                info["position"] = new[] { Round(t[0] * mm), Round(t[1] * mm), Round(t[2] * mm) };
                info["rotation"] = local.RotationDegrees();
            }
            catch (COMException) { }
            try
            {
                o.GetExtents(out IADPoint lo, out IADPoint hi);
                info["min"] = Mm(lo, mm);
                info["max"] = Mm(hi, mm);
            }
            catch (COMException) { }
            return info;
        }

        object OccurrenceInfoMethod(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            return OccurrenceInfo(FindOccurrence(assembly, args.String("occurrence")), MmPerUnit((IADDesignSession)assembly));
        }

        // Places an occurrence at a position and rotation in its parent, or moves it by them (relative).
        object MoveOccurrence(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            double mm = MmPerUnit((IADDesignSession)assembly);
            IADOccurrence o = FindOccurrence(assembly, args.String("occurrence"));
            Placement current = Placement.From(o.LocalTransform);
            Placement target = args.Bool("relative", false)
                ? current.Then(Placement.FromPose(
                      (args.OptionalNumber("x") ?? 0) / mm, (args.OptionalNumber("y") ?? 0) / mm, (args.OptionalNumber("z") ?? 0) / mm,
                      args.OptionalNumber("rx") ?? 0, args.OptionalNumber("ry") ?? 0, args.OptionalNumber("rz") ?? 0))
                : PoseFrom(args, mm, current);
            ApplyPlacement(o, current, target, ((IADSession)assembly).GeometryFactory);
            return OccurrenceInfo(o, mm);
        }

        // ApplyTransform's meaning (move by, or move to) isn't documented: this moves by the change
        // from current to target, then checks, and if it overshot it was absolute, so sets the target.
        static void ApplyPlacement(IADOccurrence o, Placement current, Placement target, IADGeometryFactory factory)
        {
            Placement change = current.Inverse().Then(target);
            o.ApplyTransform(change.ToTransform(factory));
            Placement now = Placement.From(o.LocalTransform);
            if (!Same(now, target))
            {
                Log.Write("move", "ApplyTransform didn't move by the change; applying the target as given");
                o.ApplyTransform(target.ToTransform(factory));
                now = Placement.From(o.LocalTransform);
                if (!Same(now, target))
                    Log.Write("move", "the occurrence isn't at the target either (constraints may hold it)");
            }
            else
                Log.Write("move", "ApplyTransform moves by the change given (relative)");
        }

        static bool Same(Placement a, Placement b)
        {
            for (int i = 0; i < 16; ++i)
                if (Math.Abs(a.M[i] - b.M[i]) > 1e-6)
                    return false;
            return true;
        }

        object SetOccurrence(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            IADOccurrence o = FindOccurrence(assembly, args.String("occurrence"));
            if (args.Has("hidden")) o.IsHidden = args.Bool("hidden", false);
            if (args.Has("suppressed")) o.IsSuppressed = args.Bool("suppressed", false);
            if (args.Has("anchored")) o.IsAnchored = args.Bool("anchored", false);
            return OccurrenceInfo(o, MmPerUnit((IADDesignSession)assembly));
        }

        // ---- constraints ----

        // One side of a constraint: {occurrence, face | edge | plane | axis | point}. Faces and edges
        // are list_topology indexes in the occurrence's part; plane, axis and point are design geometry
        // names in it. With no occurrence, plane, axis and point are the assembly's own.
        IADTargetProxy ConstraintTarget(IADAssemblySession assembly, Args side, string label)
        {
            if (side == null)
                throw new ArgumentException("'" + label + "' is required: {occurrence, and one of face, edge, plane, axis, point}.");
            IADOccurrence occurrence = side.Has("occurrence") ? FindOccurrence(assembly, side.String("occurrence")) : assembly.RootOccurrence;
            IADDesignSession design = occurrence.DesignSession;
            object target;
            if (side.Has("face") || side.Has("edge"))
            {
                var part = design as IADPartSession ??
                           throw new ArgumentException("Faces and edges in '" + label + "' need an occurrence that's a part.");
                target = side.Has("face") ? (object)FaceAt(part, side.Int("face", -1)) : EdgeAt(part, side.Int("edge", -1));
            }
            else
            {
                string name = side.OptionalString("plane") ?? side.OptionalString("axis") ?? side.OptionalString("point") ??
                              throw new ArgumentException("'" + label + "' needs a face, edge, plane, axis or point.");
                target = DesignGeometry(design, name);
            }
            return ((IADSession)assembly).NewTargetProxy(occurrence, target);
        }

        static object DesignGeometry(IADDesignSession design, string name)
        {
            foreach (IADDesignPlane p in design.DesignPlanes)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            foreach (IADDesignAxis a in design.DesignAxes)
                if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            foreach (IADDesignPoint p in design.DesignPoints)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            throw new ArgumentException("'" + ((IADSession)design).Name + "' has no design plane, axis or point called '" + name + "'.");
        }

        static readonly Dictionary<string, ADAssemblyConstraintType> s_constraintTypes =
            new Dictionary<string, ADAssemblyConstraintType>(StringComparer.OrdinalIgnoreCase)
            {
                ["mate"] = ADAssemblyConstraintType.AD_MATE_TYPE,
                ["align"] = ADAssemblyConstraintType.AD_ALIGN_TYPE,
                ["orient"] = ADAssemblyConstraintType.AD_ORIENT_TYPE,
                ["angle"] = ADAssemblyConstraintType.AD_ANGLE_TYPE,
                ["tangentInside"] = ADAssemblyConstraintType.AD_TANGENT_INSIDE_TYPE,
                ["tangentOutside"] = ADAssemblyConstraintType.AD_TANGENT_OUTSIDE_TYPE,
                ["gear"] = ADAssemblyConstraintType.AD_GEAR_TYPE,
            };

        // A gear constraint couples two rotations by ratio1 : ratio2 (two parameters, so AddConstraintEx).
        // The bound type it wants isn't documented for 27.0.1: equality first, then the others, and the
        // one that worked is logged.
        static IADAssemblyConstraint AddGearConstraint(IADAssemblySession assembly, IADTargetProxy first, IADTargetProxy second,
                                                       double ratio1, double ratio2, bool reversed, string name,
                                                       string parameterName, string parameter2Name)
        {
            var bounds = new[]
            {
                ADAssemblyConstraintBoundType.AD_EQUALITY_TYPE,
                ADAssemblyConstraintBoundType.AD_LOGICAL_TYPE,
                ADAssemblyConstraintBoundType.AD_UNKNOWN_TYPE,
            };
            COMException last = null;
            foreach (var bound in bounds)
            {
                try
                {
                    var c = assembly.AssemblyConstraints.AddConstraintEx(first, second, ADAssemblyConstraintType.AD_GEAR_TYPE, bound,
                                                                         ratio1, ratio2, reversed, name, parameterName, parameter2Name);
                    Log.Write("constraint", "gear constraint added with bound type " + bound);
                    return c;
                }
                catch (COMException e)
                {
                    Log.Write("constraint", "gear constraint with bound type " + bound + " refused: 0x" + e.HResult.ToString("X8") + " " + e.Message);
                    DeleteConstraintIfLeft(assembly, name);
                    last = e;
                }
            }
            throw last;
        }

        object AddAssemblyConstraint(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            var design = (IADDesignSession)assembly;
            double u = 1.0 / MmPerUnit(design);
            string typeName = args.String("type");
            if (!s_constraintTypes.TryGetValue(typeName, out var type))
                throw new ArgumentException("Unknown constraint type '" + typeName + "' (" + string.Join(", ", s_constraintTypes.Keys) + ").");

            // Names first: listing after the targets are bound could leave them stale (as with features).
            string name = args.OptionalString("name") ?? UnusedConstraintName(assembly, char.ToUpperInvariant(typeName[0]) + typeName.Substring(1));
            string parameterName = UnusedParameterName((IADSession)assembly, "C");
            IADTargetProxy first = ConstraintTarget(assembly, args.Object("first"), "first");
            IADTargetProxy second = ConstraintTarget(assembly, args.Object("second"), "second");
            double value = args.OptionalNumber("value") ?? 0;
            object parameter = type == ADAssemblyConstraintType.AD_ANGLE_TYPE ? Angle(value) : value * u;

            IADAssemblyConstraint constraint;
            try
            {
                if (type == ADAssemblyConstraintType.AD_GEAR_TYPE)
                {
                    double ratio1 = args.OptionalNumber("ratio1") ?? 1;
                    double ratio2 = args.OptionalNumber("ratio2") ?? 1;
                    if (ratio1 <= 0 || ratio2 <= 0)
                        throw new ArgumentException("A gear constraint's ratio1 and ratio2 must be greater than 0.");
                    string parameter2Name = UnusedParameterName((IADSession)assembly, "C", parameterName);
                    constraint = AddGearConstraint(assembly, first, second, ratio1, ratio2, args.Bool("reversed", false),
                                                   name, parameterName, parameter2Name);
                }
                else
                    constraint = assembly.AssemblyConstraints.AddConstraint(first, second, type, parameter, args.Bool("reversed", false),
                                                                            name, parameterName);
            }
            catch (COMException)
            {
                DeleteConstraintIfLeft(assembly, name);
                throw;
            }
            if (args.Bool("regenerate", true))
                design.RegenerateDesign(false);
            return ConstraintInfo(constraint);
        }

        static string UnusedConstraintName(IADAssemblySession assembly, string kind)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IADAssemblyConstraint c in assembly.AssemblyConstraints)
                used.Add(c.Name);
            for (int n = 1; ; ++n)
                if (!used.Contains(kind + "<" + n + ">"))
                    return kind + "<" + n + ">";
        }

        static void DeleteConstraintIfLeft(IADAssemblySession assembly, string name)
        {
            foreach (IADAssemblyConstraint c in assembly.AssemblyConstraints)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    try { c.Delete(); Log.Write("constraint", "deleted the refused " + name); }
                    catch (COMException) { }
                    return;
                }
        }

        static Dictionary<string, object> ConstraintInfo(IADAssemblyConstraint c)
        {
            var info = new Dictionary<string, object>
            {
                ["name"] = c.Name,
                ["type"] = EnumName(c.ConstraintType),
                ["suppressed"] = c.IsSuppressed,
                ["hasError"] = c.HasError,
            };
            try
            {
                var participants = new List<object>();
                foreach (object p in ItemsOf(c.Participants))
                    if (p is IADTargetProxy proxy)
                        participants.Add(new Dictionary<string, object>
                        {
                            ["occurrence"] = proxy.Occurrence == null || proxy.Occurrence.NestLevel == 0 ? null : PathOf(proxy.Occurrence),
                            ["target"] = proxy.DisplayName,
                        });
                info["participants"] = participants;
            }
            catch (COMException) { }
            if (c is IADGearConstraint gear)
            {
                try
                {
                    info["ratio1"] = gear.Ratio1?.Value;
                    info["ratio2"] = gear.Ratio2?.Value;
                }
                catch (COMException) { }
            }
            return info;
        }

        IADAssemblyConstraint FindConstraint(IADAssemblySession assembly, string name)
        {
            var names = new List<string>();
            foreach (IADAssemblyConstraint c in assembly.AssemblyConstraints)
            {
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    return c;
                names.Add(c.Name);
            }
            throw new ArgumentException("No constraint called '" + name + "'. The assembly has: " + Join(names) + ".");
        }

        object ListAssemblyConstraints(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            var list = new List<object>();
            foreach (IADAssemblyConstraint c in assembly.AssemblyConstraints)
                list.Add(ConstraintInfo(c));
            return list;
        }

        object SuppressAssemblyConstraint(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            IADAssemblyConstraint c = FindConstraint(assembly, args.String("constraint"));
            c.IsSuppressed = args.Bool("suppressed", true);
            ((IADDesignSession)assembly).RegenerateDesign(false);
            return ConstraintInfo(c);
        }

        object DeleteAssemblyConstraint(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            IADAssemblyConstraint c = FindConstraint(assembly, args.String("constraint"));
            string name = c.Name;
            c.Delete();
            return new Dictionary<string, object> { ["deleted"] = name };
        }

        // ---- interference ----

        object CheckInterference(Args args)
        {
            IADAssemblySession assembly = FindAssembly(args);
            double mm = MmPerUnit((IADDesignSession)assembly);
            object group1 = Group(assembly, args.Strings("occurrences"));
            object group2 = Group(assembly, args.Strings("against"));
            IADInterferences found = assembly.CheckInterference(ref group1, ref group2);
            var list = new List<object>();
            int count = found.Count, first = 0;
            if (count > 0)
            {
                try { found.Item(0); } catch (COMException) { first = 1; } // 0- or 1-based is unproven
            }
            for (int i = first; i < first + count; ++i)
            {
                IADInterference hit = found.Item(i);
                var info = new Dictionary<string, object>
                {
                    ["first"] = PathOf(hit.Part1),
                    ["second"] = PathOf(hit.Part2),
                    // The docs say cm^3 whatever the units.
                    ["volumeMm3"] = Round(hit.InterferenceVolume * 1000.0),
                };
                try
                {
                    hit.GetExtents(out IADPoint lo, out IADPoint hi);
                    info["min"] = Mm(lo, mm);
                    info["max"] = Mm(hi, mm);
                }
                catch (COMException) { }
                list.Add(info);
            }
            return new Dictionary<string, object> { ["interferences"] = list };
        }

        object Group(IADAssemblySession assembly, List<string> paths)
        {
            if (paths.Count == 0)
                return null; // the whole assembly
            return Collect(paths.ConvertAll(p => (object)FindOccurrence(assembly, p)));
        }
    }
}
