using System;

namespace SomniumSpace.Worlds.SomniumWoW.Models
{
    /// <summary>
    /// Samples M2 tracks (pure). time is ms inside the playing sequence; globalTime is ms since start (for tracks on a
    /// global sequence, which loop over GlobalSequences[gs] independently of the playing sequence). Step for
    /// interpolation 0, linear/slerp otherwise (bezier/hermite keys are sampled linearly between their key values).
    /// </summary>
    public static class M2Sampler
    {
        /// <summary>Key index k with times[k] &lt;= t &lt; times[k+1] (0 before the first key, last after the last).</summary>
        public static int FindKey(int[] times, int t)
        {
            int lo = 0, hi = times.Length - 1;
            if (t <= times[0]) return 0;
            if (t >= times[hi]) return hi;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (times[mid] <= t) lo = mid; else hi = mid;
            }
            return lo;
        }

        static int LocalTime<T>(M2Track<T> tr, int time, int[] globalSeqs, long globalTime)
        {
            if (tr.GlobalSequence < 0) return time;
            int len = globalSeqs != null && tr.GlobalSequence < globalSeqs.Length ? globalSeqs[tr.GlobalSequence] : 0;
            return len > 0 ? (int)(globalTime % len) : 0;
        }

        public static bool Sample(M2Track<M2Vec3> tr, int seq, int time, int[] globalSeqs, long globalTime, out M2Vec3 v)
        {
            int[] times; M2Vec3[] vals;
            v = default(M2Vec3);
            if (tr == null || !tr.TryGetKeys(seq, out times, out vals)) return false;
            int t = LocalTime(tr, time, globalSeqs, globalTime);
            int k = FindKey(times, t);
            if (tr.Interpolation == 0 || k >= vals.Length - 1 || t <= times[k]) { v = vals[Math.Min(k, vals.Length - 1)]; return true; }
            float f = (t - times[k]) / (float)Math.Max(1, times[k + 1] - times[k]);
            M2Vec3 a = vals[k], b = vals[k + 1];
            v = new M2Vec3(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f);
            return true;
        }

        public static bool Sample(M2Track<M2Quat> tr, int seq, int time, int[] globalSeqs, long globalTime, out M2Quat q)
        {
            int[] times; M2Quat[] vals;
            q = M2Quat.Identity;
            if (tr == null || !tr.TryGetKeys(seq, out times, out vals)) return false;
            int t = LocalTime(tr, time, globalSeqs, globalTime);
            int k = FindKey(times, t);
            if (tr.Interpolation == 0 || k >= vals.Length - 1 || t <= times[k]) { q = vals[Math.Min(k, vals.Length - 1)]; return true; }
            float f = (t - times[k]) / (float)Math.Max(1, times[k + 1] - times[k]);
            q = Slerp(vals[k], vals[k + 1], f);
            return true;
        }

        public static bool Sample(M2Track<float> tr, int seq, int time, int[] globalSeqs, long globalTime, out float v)
        {
            int[] times; float[] vals;
            v = 0f;
            if (tr == null || !tr.TryGetKeys(seq, out times, out vals)) return false;
            int t = LocalTime(tr, time, globalSeqs, globalTime);
            int k = FindKey(times, t);
            if (tr.Interpolation == 0 || k >= vals.Length - 1 || t <= times[k]) { v = vals[Math.Min(k, vals.Length - 1)]; return true; }
            float f = (t - times[k]) / (float)Math.Max(1, times[k + 1] - times[k]);
            v = vals[k] + (vals[k + 1] - vals[k]) * f;
            return true;
        }

        public static M2Quat Slerp(M2Quat a, M2Quat b, float t)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot < 0f) { b = new M2Quat(-b.X, -b.Y, -b.Z, -b.W); dot = -dot; }
            float wa, wb;
            if (dot > 0.9995f) { wa = 1f - t; wb = t; }
            else
            {
                float th = (float)Math.Acos(dot), s = (float)Math.Sin(th);
                wa = (float)Math.Sin((1f - t) * th) / s; wb = (float)Math.Sin(t * th) / s;
            }
            var r = new M2Quat(a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Z * wa + b.Z * wb, a.W * wa + b.W * wb);
            float len = (float)Math.Sqrt(r.X * r.X + r.Y * r.Y + r.Z * r.Z + r.W * r.W);
            return len > 1e-6f ? new M2Quat(r.X / len, r.Y / len, r.Z / len, r.W / len) : M2Quat.Identity;
        }
    }

    /// <summary>
    /// Bone matrices of one pose in WoW model space (pure; used to bake the resting pose of static doodads):
    /// M_bone = M_parent * T(pivot) * T(translation) * R(rotation) * S(scale) * T(-pivot). Column-major 4x4, 16 floats per bone.
    /// </summary>
    public static class M2Pose
    {
        /// <summary>The sequence a static/rest pose uses: Stand (0) when present, else the first non-alias sequence, else -1.</summary>
        public static int RestSequence(M2Model m)
        {
            int s = m.FindSequence(0);
            if (s >= 0) s = m.ResolveAlias(s);
            if (s >= 0 && m.Sequences[s].DataLoaded) return s;
            for (int i = 0; i < m.Sequences.Length; i++) if (!m.Sequences[i].IsAlias && m.Sequences[i].DataLoaded) return i;
            return -1;
        }

        /// <summary>16 floats per bone. seq -1 = bind pose (identity everywhere).</summary>
        public static float[] Compute(M2Model m, int seq, int time, long globalTime)
        {
            int n = m.Bones.Length;
            var mats = new float[n * 16];
            var done = new bool[n];
            for (int i = 0; i < n; i++) ComputeBone(m, i, seq, time, globalTime, mats, done, 0);
            return mats;
        }

        static void ComputeBone(M2Model m, int i, int seq, int time, long globalTime, float[] mats, bool[] done, int depth)
        {
            if (done[i]) return;
            var b = m.Bones[i];
            M2Vec3 t = new M2Vec3(0, 0, 0), s = new M2Vec3(1, 1, 1);
            M2Quat q = M2Quat.Identity;
            if (seq >= 0)
            {
                M2Vec3 v; M2Quat r;
                if (M2Sampler.Sample(b.Translation, seq, time, m.GlobalSequences, globalTime, out v)) t = v;
                if (M2Sampler.Sample(b.Rotation, seq, time, m.GlobalSequences, globalTime, out r)) q = r;
                if (M2Sampler.Sample(b.Scale, seq, time, m.GlobalSequences, globalTime, out v)) s = v;
            }
            var local = new float[16];
            Trs(b.Pivot, t, q, s, local);
            int p = b.Parent;
            if (p >= 0 && p < m.Bones.Length && depth < 256)
            {
                ComputeBone(m, p, seq, time, globalTime, mats, done, depth + 1);
                var parent = new float[16];
                Array.Copy(mats, p * 16, parent, 0, 16);
                var res = new float[16];
                Mul(parent, local, res);
                Array.Copy(res, 0, mats, i * 16, 16);
            }
            else Array.Copy(local, 0, mats, i * 16, 16);
            done[i] = true;
        }

        /// <summary>T(pivot) T(t) R(q) S(s) T(-pivot), column-major.</summary>
        public static void Trs(M2Vec3 pivot, M2Vec3 t, M2Quat q, M2Vec3 s, float[] o)
        {
            float x = q.X, y = q.Y, z = q.Z, w = q.W;
            float r00 = 1 - 2 * (y * y + z * z), r01 = 2 * (x * y - z * w), r02 = 2 * (x * z + y * w);
            float r10 = 2 * (x * y + z * w), r11 = 1 - 2 * (x * x + z * z), r12 = 2 * (y * z - x * w);
            float r20 = 2 * (x * z - y * w), r21 = 2 * (y * z + x * w), r22 = 1 - 2 * (x * x + y * y);
            // A = R * S
            float a00 = r00 * s.X, a01 = r01 * s.Y, a02 = r02 * s.Z;
            float a10 = r10 * s.X, a11 = r11 * s.Y, a12 = r12 * s.Z;
            float a20 = r20 * s.X, a21 = r21 * s.Y, a22 = r22 * s.Z;
            // translation = pivot + t - A * pivot
            float tx = pivot.X + t.X - (a00 * pivot.X + a01 * pivot.Y + a02 * pivot.Z);
            float ty = pivot.Y + t.Y - (a10 * pivot.X + a11 * pivot.Y + a12 * pivot.Z);
            float tz = pivot.Z + t.Z - (a20 * pivot.X + a21 * pivot.Y + a22 * pivot.Z);
            o[0] = a00; o[1] = a10; o[2] = a20; o[3] = 0;
            o[4] = a01; o[5] = a11; o[6] = a21; o[7] = 0;
            o[8] = a02; o[9] = a12; o[10] = a22; o[11] = 0;
            o[12] = tx; o[13] = ty; o[14] = tz; o[15] = 1;
        }

        public static void Mul(float[] a, float[] b, float[] o)
        {
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                    o[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
        }

        /// <summary>
        /// Skins positions/normals of the model with the given bone matrices (linear blend, weights /255). Output arrays are
        /// 3 floats per M2 vertex. Normals are transformed by the upper 3x3 and renormalised.
        /// </summary>
        public static void Skin(M2Model m, float[] mats, float[] outPos, float[] outNrm)
        {
            int n = m.VertexCount, nb = m.Bones.Length;
            for (int v = 0; v < n; v++)
            {
                float px = m.Positions[v * 3], py = m.Positions[v * 3 + 1], pz = m.Positions[v * 3 + 2];
                float nx = m.Normals[v * 3], ny = m.Normals[v * 3 + 1], nz = m.Normals[v * 3 + 2];
                float ox = 0, oy = 0, oz = 0, qx = 0, qy = 0, qz = 0, wsum = 0;
                for (int k = 0; k < 4; k++)
                {
                    int wt = m.BoneWeights[v * 4 + k];
                    if (wt == 0) continue;
                    int bi = m.BoneIndices[v * 4 + k];
                    if (bi >= nb) continue;
                    float w = wt / 255f;
                    int o = bi * 16;
                    ox += w * (mats[o] * px + mats[o + 4] * py + mats[o + 8] * pz + mats[o + 12]);
                    oy += w * (mats[o + 1] * px + mats[o + 5] * py + mats[o + 9] * pz + mats[o + 13]);
                    oz += w * (mats[o + 2] * px + mats[o + 6] * py + mats[o + 10] * pz + mats[o + 14]);
                    qx += w * (mats[o] * nx + mats[o + 4] * ny + mats[o + 8] * nz);
                    qy += w * (mats[o + 1] * nx + mats[o + 5] * ny + mats[o + 9] * nz);
                    qz += w * (mats[o + 2] * nx + mats[o + 6] * ny + mats[o + 10] * nz);
                    wsum += w;
                }
                if (wsum < 1e-4f) { ox = px; oy = py; oz = pz; qx = nx; qy = ny; qz = nz; }
                else if (Math.Abs(wsum - 1f) > 1e-3f) { ox /= wsum; oy /= wsum; oz /= wsum; }
                float len = (float)Math.Sqrt(qx * qx + qy * qy + qz * qz);
                if (len > 1e-6f) { qx /= len; qy /= len; qz /= len; } else { qx = nx; qy = ny; qz = nz; }
                outPos[v * 3] = ox; outPos[v * 3 + 1] = oy; outPos[v * 3 + 2] = oz;
                outNrm[v * 3] = qx; outNrm[v * 3 + 1] = qy; outNrm[v * 3 + 2] = qz;
            }
        }
    }
}
