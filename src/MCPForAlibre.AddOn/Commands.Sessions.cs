using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AlibreX;

namespace MCPForAlibre.AddOn
{
    // Sessions, part summaries, parameters, assemblies and the selection.
    sealed partial class Commands
    {
        void RegisterSessions()
        {
            m_methods["status"] = Status;
            m_methods["open_file"] = OpenFile;
            m_methods["save"] = Save;
            m_methods["close"] = Close;
            m_methods["create_part"] = CreatePart;
            m_methods["part_summary"] = PartSummary;
            m_methods["list_parameters"] = ListParameters;
            m_methods["set_parameter"] = SetParameter;
            m_methods["create_parameter"] = CreateParameter;
            m_methods["assembly_tree"] = AssemblyTree;
            m_methods["selection"] = Selection;
        }

        object Status(Args args)
        {
            var sessions = new List<object>();
            foreach (IADSession s in m_root.Sessions)
                sessions.Add(SessionInfo(s));
            IADSession top = null;
            try { top = m_root.TopmostSession; } catch (COMException) { }
            return new Dictionary<string, object>
            {
                ["alibreVersion"] = m_root.Version,
                ["sessions"] = sessions,
                ["activeSession"] = top?.Name,
            };
        }

        object OpenFile(Args args)
        {
            IADSession s = m_root.OpenFileEx(args.String("path"), args.Bool("show", true));
            return SessionInfo(s);
        }

        object Save(Args args)
        {
            IADSession s = FindSession(args);
            string folder = args.OptionalString("folder");
            if (!string.IsNullOrEmpty(folder))
            {
                if (!System.IO.Directory.Exists(folder))
                    throw new ArgumentException("The folder '" + folder + "' doesn't exist.");
                object destination = folder;
                if (args.Has("name"))
                    s.SaveAs(ref destination, args.String("name")); // a copy under a new name
                else if (string.IsNullOrEmpty(s.FilePath))
                    s.SaveNew(ref destination); // first save, with any new parts inside it
                else
                    s.SaveAs(ref destination, s.Name);
                return SessionInfo(s);
            }
            if (string.IsNullOrEmpty(s.FilePath))
                throw new InvalidOperationException("'" + s.Name + "' has never been saved. Give a folder to save it in.");
            s.Save();
            return SessionInfo(s);
        }

        object Close(Args args)
        {
            IADSession s = FindSession(args);
            string name = s.Name;
            s.Close(args.Bool("save", false));
            return new Dictionary<string, object> { ["closed"] = name };
        }

        object CreatePart(Args args)
        {
            IADPartSession part = m_root.CreateEmptyPartEx(args.String("name"), args.Bool("sheetMetal", false), true);
            return SessionInfo((IADSession)part);
        }

        static Dictionary<string, object> SessionInfo(IADSession s)
        {
            var info = new Dictionary<string, object>
            {
                ["name"] = s.Name,
                ["kind"] = Kind(s.SessionType),
                ["filePath"] = s.FilePath,
                ["identifier"] = s.Identifier,
            };
            if (s is IADDesignSession d)
                info["modelUnits"] = UnitName(d.DesignProperties.ModelUnits);
            return info;
        }

        object PartSummary(Args args)
        {
            IADPartSession part = FindPart(args);
            var design = (IADDesignSession)part;
            double mm = MmPerUnit(design);

            var features = new List<object>();
            foreach (IADPartFeature f in part.Features)
                features.Add(new Dictionary<string, object>
                {
                    ["name"] = f.Name,
                    ["type"] = EnumName(f.FeatureType),
                    ["suppressed"] = f.IsSuppressed,
                    ["hasError"] = f.HasError,
                });

            var sketches = new List<object>();
            foreach (IADSketch k in part.Sketches)
                sketches.Add(new Dictionary<string, object>
                {
                    ["name"] = k.Name,
                    ["figures"] = k.Figures.Count,
                    ["closed"] = k.IsClosed,
                    ["consumed"] = k.IsConsumed,
                });

            var planes = new List<object>();
            foreach (IADDesignPlane p in design.DesignPlanes)
                planes.Add(p.Name);
            var axes = new List<object>();
            foreach (IADDesignAxis a in design.DesignAxes)
                axes.Add(a.Name);
            var points = new List<object>();
            foreach (IADDesignPoint p in design.DesignPoints)
                points.Add(new Dictionary<string, object> { ["name"] = p.Name, ["at"] = Mm(p.Geometry, mm) });

            var summary = new Dictionary<string, object>
            {
                ["session"] = SessionInfo((IADSession)part),
                ["features"] = features,
                ["sketches"] = sketches,
                ["designPlanes"] = planes,
                ["designAxes"] = axes,
                ["designPoints"] = points,
                ["bodies"] = part.Bodies.Count,
                ["hasErrors"] = design.HasErrors,
            };
            try
            {
                var phys = design.PhysicalProperties(ADAccuracySetting.AD_MEDIUM);
                summary["volumeMm3"] = Round(phys.Volume * mm * mm * mm);
            }
            catch (COMException) { }
            return summary;
        }

        object ListParameters(Args args)
        {
            IADSession s = FindSession(args);
            double mm = s is IADDesignSession d ? MmPerUnit(d) : 10.0;
            var list = new List<object>();
            foreach (IADParameter p in s.Parameters)
                list.Add(ParameterInfo(p, mm));
            return list;
        }

        // A new parameter, such as Width = 10 mm, which equations and dimensions can then use.
        object CreateParameter(Args args)
        {
            IADSession s = FindSession(args);
            string name = args.String("name");
            foreach (IADParameter existing in s.Parameters)
                if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("'" + s.Name + "' already has a parameter called '" + name + "'; use set_parameter.");
            string typeName = args.OptionalString("type") ?? "distance";
            if (!Enum.TryParse("AD_" + typeName.ToUpperInvariant(), out ADParameterType type))
                throw new ArgumentException("Unknown parameter type '" + typeName + "' (distance, angle, count, scale).");

            // NewParameter opens its own transaction (it fails with "Parameter transaction already open"
            // inside one), but setting the new parameter's equation needs one: so make it first, then set it.
            IADParameter created = s.Parameters.NewParameter(name, type);
            ParameterChange(s, () =>
            {
                if (args.Has("equation"))
                    created.Equation = args.String("equation");
                else
                    created.Value = args.Number("value");
                if (args.Has("comment"))
                    created.comment = args.String("comment");
            });
            if (s is IADDesignSession d)
                d.RegenerateDesign(false);
            return ParameterInfo(created, s is IADDesignSession dd ? MmPerUnit(dd) : 10.0);
        }

        object SetParameter(Args args)
        {
            IADSession s = FindSession(args);
            string name = args.String("name");
            IADParameter found = null;
            foreach (IADParameter p in s.Parameters)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                    found = p;
            if (found == null)
                throw new ArgumentException("'" + s.Name + "' has no parameter called '" + name + "'.");

            ParameterChange(s, () =>
            {
                if (args.Has("equation"))
                    found.Equation = args.String("equation");
                else
                    found.Value = args.Number("value");
            });
            if (s is IADDesignSession d && args.Bool("regenerate", true))
                d.RegenerateDesign(false);
            return ParameterInfo(found, s is IADDesignSession dd ? MmPerUnit(dd) : 10.0);
        }

        // Every parameter change needs an open parameter transaction ("No opened transaction found"),
        // and a new part can already have one open ("Parameter transaction already open"). So open one
        // if none is, and close (or on failure cancel) only one opened here. Proven 2026-11-10.
        static void ParameterChange(IADSession session, Action change)
        {
            IADParameters parameters = session.Parameters;
            bool opened = false;
            try
            {
                parameters.OpenParameterTransaction();
                opened = true;
            }
            catch (COMException ex) when ((uint)ex.ErrorCode == 0x80040218)
            {
                Log.Write("parameters", "a parameter transaction was already open; using it");
            }
            try
            {
                change();
                if (opened)
                    parameters.CloseParameterTransaction();
            }
            catch
            {
                if (opened)
                {
                    try { parameters.CancelParameterTransaction(); } catch (COMException) { }
                }
                throw;
            }
        }

        // value is as the API gives it; mm (distances, taking it as model units) and degrees (angles,
        // taking it as radians) are worked out from it. Both readings are checked in the equation tests.
        static Dictionary<string, object> ParameterInfo(IADParameter p, double mm)
        {
            var info = new Dictionary<string, object>
            {
                ["name"] = p.Name,
                ["type"] = EnumName(p.ParameterType),
                ["value"] = p.Value,
                ["units"] = UnitName(p.Units),
                ["equation"] = p.Equation,
                ["comment"] = p.comment,
            };
            if (p.ParameterType == ADParameterType.AD_DISTANCE)
                info["mm"] = Round(p.Value * mm);
            else if (p.ParameterType == ADParameterType.AD_ANGLE)
                info["degrees"] = Degrees(p.Value);
            return info;
        }

        object AssemblyTree(Args args)
        {
            var assembly = FindSession(args) as IADAssemblySession
                           ?? throw new InvalidOperationException("That session isn't an assembly.");
            return Occurrence(assembly.RootOccurrence, args.Int("depth", 10), MmPerUnit((IADDesignSession)assembly));
        }

        static Dictionary<string, object> Occurrence(IADOccurrence o, int depth, double mm)
        {
            var info = OccurrenceInfo(o, mm);
            if (depth > 0 && o.Occurrences.Count > 0)
            {
                var children = new List<object>();
                foreach (IADOccurrence child in o.Occurrences)
                    children.Add(Occurrence(child, depth - 1, mm));
                info["children"] = children;
            }
            return info;
        }

        object Selection(Args args)
        {
            IADSession s = FindSession(args);
            var part = s as IADPartSession;
            List<IADFace> faces = part != null ? AllFaces(part) : null;
            List<IADEdge> edges = part != null ? AllEdges(part) : null;
            var list = new List<object>();
            foreach (object item in ItemsOf(s.SelectedObjects))
                list.Add(DescribeItem(item, faces, edges));
            return list;
        }

        // In a part, faces and edges also get their list_topology index (found by comparing COM identity).
        static Dictionary<string, object> DescribeItem(object item, List<IADFace> faces, List<IADEdge> edges)
        {
            var info = new Dictionary<string, object>();
            if (item is IADTargetProxy proxy)
            {
                IADOccurrence occ = proxy.Occurrence;
                info["occurrence"] = occ == null || occ.NestLevel == 0 ? null : PathOf(occ);
                info["displayName"] = proxy.DisplayName;
                item = proxy.Target;
                // In an assembly, number faces and edges in the occurrence's own part (list_topology occurrence).
                if (faces == null && occ != null && (item is IADFace || item is IADEdge))
                {
                    try
                    {
                        if (occ.DesignSession is IADPartSession part)
                        {
                            faces = AllFaces(part);
                            edges = AllEdges(part);
                        }
                    }
                    catch (COMException) { }
                }
            }
            switch (item)
            {
                case IADFace face:
                    info["kind"] = "face";
                    if (faces != null) info["index"] = IndexOf(faces, face);
                    break;
                case IADEdge edge:
                    info["kind"] = "edge";
                    if (edges != null) info["index"] = IndexOf(edges, edge);
                    break;
                case IADOccurrence o: info["kind"] = "occurrence"; info["name"] = o.Name; break;
                case IADPartFeature f: info["kind"] = "feature"; info["name"] = f.Name; break;
                case IADSketch k: info["kind"] = "sketch"; info["name"] = k.Name; break;
                case IADDesignPlane p: info["kind"] = "designPlane"; info["name"] = p.Name; break;
                case IADDesignAxis a: info["kind"] = "designAxis"; info["name"] = a.Name; break;
                case IADDesignPoint p: info["kind"] = "designPoint"; info["name"] = p.Name; break;
                case IADSketchFigure g: info["kind"] = "sketchFigure"; info["id"] = FigureId(g); info["figureType"] = EnumName(g.FigureType); break;
                default: info["kind"] = "other"; break;
            }
            return info;
        }

        // COM objects for the same face can be different wrappers; compare their IUnknown pointers.
        static int IndexOf<T>(List<T> items, T item) where T : class
        {
            IntPtr wanted = Marshal.GetIUnknownForObject(item);
            try
            {
                for (int i = 0; i < items.Count; ++i)
                {
                    IntPtr p = Marshal.GetIUnknownForObject(items[i]);
                    Marshal.Release(p);
                    if (p == wanted)
                        return i;
                }
                return -1;
            }
            finally
            {
                Marshal.Release(wanted);
            }
        }
    }
}
