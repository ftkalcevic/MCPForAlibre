using System;
using AlibreX;

namespace MCPForAlibre.AddOn
{
    // A rigid placement as Alibre stores it: 16 numbers by rows, points as row vectors (p * M),
    // the translation in the fourth row (indexes 12 to 14), in the assembly's model units
    // (proven in CAMForAlibre's PartToAssembly). Rotations are about the assembly's X, then Y, then Z.
    sealed class Placement
    {
        public readonly double[] M = new double[16];

        public static Placement Identity()
        {
            var p = new Placement();
            p.M[0] = p.M[5] = p.M[10] = p.M[15] = 1;
            return p;
        }

        public static Placement From(IADTransformation t)
        {
            var p = new Placement();
            Array a = t.Array();
            int i = 0;
            foreach (object v in a)
            {
                if (i >= 16) break;
                p.M[i++] = Convert.ToDouble(v);
            }
            if (i == 12) // a 4 x 3 array: no last column
            {
                var m = new double[16];
                for (int r = 0; r < 4; ++r)
                    for (int c = 0; c < 3; ++c)
                        m[r * 4 + c] = p.M[r * 3 + c];
                m[15] = 1;
                Array.Copy(m, p.M, 16);
            }
            return p;
        }

        // Rotation by rx, ry, rz degrees about X, then Y, then Z, then a move by (x, y, z).
        public static Placement FromPose(double x, double y, double z, double rx, double ry, double rz)
        {
            double[] r = Mul3(Mul3(RotX(rx), RotY(ry)), RotZ(rz));
            var p = new Placement();
            for (int row = 0; row < 3; ++row)
                for (int c = 0; c < 3; ++c)
                    p.M[row * 4 + c] = r[row * 3 + c];
            p.M[12] = x; p.M[13] = y; p.M[14] = z; p.M[15] = 1;
            return p;
        }

        // Row-vector rotations: p' = p * R.
        static double[] RotX(double deg) { double c = Cos(deg), s = Sin(deg); return new[] { 1, 0, 0, 0, c, s, 0, -s, c }; }
        static double[] RotY(double deg) { double c = Cos(deg), s = Sin(deg); return new[] { c, 0, -s, 0, 1, 0, s, 0, c }; }
        static double[] RotZ(double deg) { double c = Cos(deg), s = Sin(deg); return new[] { c, s, 0, -s, c, 0, 0, 0, 1 }; }
        static double Cos(double deg) => Math.Cos(deg * Math.PI / 180);
        static double Sin(double deg) => Math.Sin(deg * Math.PI / 180);

        static double[] Mul3(double[] a, double[] b)
        {
            var m = new double[9];
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    for (int k = 0; k < 3; ++k)
                        m[i * 3 + j] += a[i * 3 + k] * b[k * 3 + j];
            return m;
        }

        // this then other: p * this * other.
        public Placement Then(Placement other)
        {
            var p = new Placement();
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                    for (int k = 0; k < 4; ++k)
                        p.M[i * 4 + j] += M[i * 4 + k] * other.M[k * 4 + j];
            return p;
        }

        // The inverse of a rotation and move.
        public Placement Inverse()
        {
            var p = Identity();
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    p.M[i * 4 + j] = M[j * 4 + i];
            for (int j = 0; j < 3; ++j)
                p.M[12 + j] = -(M[12] * p.M[j] + M[13] * p.M[4 + j] + M[14] * p.M[8 + j]);
            return p;
        }

        public double[] Point(double x, double y, double z) => new[]
        {
            x * M[0] + y * M[4] + z * M[8] + M[12],
            x * M[1] + y * M[5] + z * M[9] + M[13],
            x * M[2] + y * M[6] + z * M[10] + M[14],
        };

        public double[] Direction(double x, double y, double z) => new[]
        {
            x * M[0] + y * M[4] + z * M[8],
            x * M[1] + y * M[5] + z * M[9],
            x * M[2] + y * M[6] + z * M[10],
        };

        public double[] Translation => new[] { M[12], M[13], M[14] };

        // Angles about X, then Y, then Z in degrees, the inverse of FromPose.
        // The column-vector matrix is the transpose: R[i][j] = M[j * 4 + i].
        public double[] RotationDegrees()
        {
            double r20 = M[2], r21 = M[6], r22 = M[10], r10 = M[1], r00 = M[0];
            double ry = Math.Asin(Math.Max(-1, Math.Min(1, -r20)));
            double rx, rz;
            if (Math.Abs(r20) < 0.999999)
            {
                rx = Math.Atan2(r21, r22);
                rz = Math.Atan2(r10, r00);
            }
            else
            {
                // Looking straight along Y: X and Z turn about the same axis; put it all in X.
                rz = 0;
                rx = Math.Atan2(-M[9], M[5]);
            }
            return new[] { Deg(rx), Deg(ry), Deg(rz) };
        }

        static double Deg(double rad)
        {
            double d = Math.Round(rad * 180 / Math.PI, 6);
            return d == 0 ? 0 : d; // no -0
        }

        public IADTransformation ToTransform(IADGeometryFactory factory)
        {
            Array a = (double[])M.Clone();
            return factory.CreateTransform(ref a);
        }
    }
}
