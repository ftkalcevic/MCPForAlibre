using System;
using System.Collections.Generic;
using AlibreX;

namespace MCPForAlibre.AddOn
{
    // Reference geometry: design planes, axes and points.
    sealed partial class Commands
    {
        void RegisterGeometry()
        {
            m_methods["create_design_plane"] = CreateDesignPlane;
            m_methods["create_design_axis"] = CreateDesignAxis;
            m_methods["create_design_point"] = CreateDesignPoint;
        }

        List<object> References(IADPartSession part, Args args, string name, int count)
        {
            var refs = args.Strings(name);
            if (refs.Count != count)
                throw new ArgumentException("'" + name + "' needs exactly " + count + " references.");
            return refs.ConvertAll(r => ResolveReference(part, r));
        }

        object CreateDesignPlane(Args args)
        {
            IADPartSession part = FindPart(args);
            double u = 1.0 / MmPerUnit(part);
            var planes = ((IADDesignSession)part).DesignPlanes;
            string name = args.OptionalString("name") ?? "";
            IADDesignPlane plane;
            string kind = args.String("kind");
            switch (kind)
            {
                case "offset":
                    plane = planes.CreateAtOffsetToPlane(null, ResolveReference(part, args.String("from")), args.Number("offset") * u, name);
                    break;
                case "angle":
                    plane = planes.CreateAtAngleToPlane(null, ResolveReference(part, args.String("from")),
                                                        null, ResolveReference(part, args.String("axis")),
                                                        Angle(args.Number("angle")), name);
                    break;
                case "threePoints":
                {
                    var p = References(part, args, "points", 3);
                    plane = planes.CreateBy3Points(null, p[0], null, p[1], null, p[2], name);
                    break;
                }
                default:
                    throw new ArgumentException("Unknown plane kind '" + kind + "' (offset, angle, threePoints).");
            }
            return new Dictionary<string, object> { ["name"] = plane.Name };
        }

        object CreateDesignAxis(Args args)
        {
            IADPartSession part = FindPart(args);
            var axes = ((IADDesignSession)part).DesignAxes;
            string name = args.OptionalString("name") ?? "";
            IADDesignAxis axis;
            string kind = args.String("kind");
            switch (kind)
            {
                case "twoPoints":
                {
                    var p = References(part, args, "points", 2);
                    axis = axes.CreateBy2Points(null, p[0], null, p[1], name);
                    break;
                }
                case "twoPlanes":
                {
                    var p = References(part, args, "planes", 2);
                    axis = axes.CreateBy2Planes(null, p[0], null, p[1], name);
                    break;
                }
                case "cylindricalFace":
                    axis = axes.CreateFromCylindricalFace(null, FaceAt(part, args.Int("face", -1)), name);
                    break;
                default:
                    throw new ArgumentException("Unknown axis kind '" + kind + "' (twoPoints, twoPlanes, cylindricalFace).");
            }
            return new Dictionary<string, object> { ["name"] = axis.Name };
        }

        object CreateDesignPoint(Args args)
        {
            IADPartSession part = FindPart(args);
            double mm = MmPerUnit(part);
            double u = 1.0 / mm;
            var points = ((IADDesignSession)part).DesignPoints;
            string name = args.OptionalString("name") ?? "";
            IADDesignPoint point;
            string kind = args.String("kind");
            switch (kind)
            {
                case "xyz":
                    point = points.CreatePoint(args.Number("x") * u, args.Number("y") * u, args.Number("z") * u, name);
                    break;
                case "circularEdge":
                    point = points.CreatePointFromCircularEdge(null, EdgeAt(part, args.Int("edge", -1)), name);
                    break;
                case "onEdge":
                    point = points.CreateOnEdge(null, EdgeAt(part, args.Int("edge", -1)), args.OptionalNumber("ratio") ?? 0.5, name);
                    break;
                case "between":
                {
                    var p = References(part, args, "points", 2);
                    point = points.CreateBetween2Points(null, p[0], null, p[1], args.OptionalNumber("ratio") ?? 0.5, name);
                    break;
                }
                case "threePlanes":
                {
                    var p = References(part, args, "planes", 3);
                    point = points.CreateBy3Planes(null, p[0], null, p[1], null, p[2], name);
                    break;
                }
                case "offset":
                    point = points.CreateAtOffsetToPoint(null, ResolveReference(part, args.String("from")),
                        (args.OptionalNumber("dx") ?? 0) * u, (args.OptionalNumber("dy") ?? 0) * u, (args.OptionalNumber("dz") ?? 0) * u, name);
                    break;
                default:
                    throw new ArgumentException("Unknown point kind '" + kind + "' (xyz, circularEdge, onEdge, between, threePlanes, offset).");
            }
            return new Dictionary<string, object> { ["name"] = point.Name, ["at"] = Mm(point.Geometry, mm) };
        }
    }
}
