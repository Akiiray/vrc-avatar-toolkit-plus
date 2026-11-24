using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    internal static class KissFaceAnalysis
    {
        internal sealed class Result
        {
            public Vector3 Mouth, Surface, Nose, Midpoint;
            public bool ReliableNose, Projected, RebuiltNormals;
            public int Visemes;
        }

        internal static Transform Head(VRCAvatarDescriptor avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            return animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        }

        internal static Result Analyze(VRCAvatarDescriptor avatar)
        {
            var head = Head(avatar);
            var renderer = avatar.VisemeSkinnedMesh;
            var mesh = renderer ? renderer.sharedMesh : null;
            if (!head || !mesh || !renderer.transform.IsChildOf(avatar.transform))
                throw new InvalidOperationException("Humanoid Head とアバター内の Viseme Renderer が必要です。");

            // BakeMesh(false) の結果を回転と平行移動でワールドへ戻す。
            // 対応する Renderer のスケールを二重適用しない。
            CheckFrame(renderer.transform);
            var scale = CheckFrame(head);
            CheckFrame(avatar.transform);
            var frame = Matrix4x4.TRS(avatar.transform.position, avatar.transform.rotation, Vector3.one);
            var toFace = frame.inverse * Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, false);
                var points = baked.vertices.Select(toFace.MultiplyPoint3x4).ToArray();
                var normals = baked.normals.Select(toFace.MultiplyVector).ToArray();
                if (points.Length != mesh.vertexCount || normals.Length != points.Length || points.Length == 0)
                    throw new InvalidOperationException("顔メッシュの頂点または法線を取得できません。");

                var regions = new List<int[]>();
                var names = avatar.VisemeBlendShapes ?? Array.Empty<string>();
                var used = new HashSet<int>();
                var deltas = new Vector3[points.Length];
                foreach (int slot in new[] { 1, 2, 10, 13, 14 })
                {
                    if (slot >= names.Length || string.IsNullOrEmpty(names[slot])) continue;
                    int index = mesh.GetBlendShapeIndex(names[slot]);
                    if (index < 0 || !used.Add(index)) continue;
                    int frames = mesh.GetBlendShapeFrameCount(index);
                    if (frames == 0) continue;
                    mesh.GetBlendShapeFrameVertices(index, frames - 1, deltas, null, null);
                    float peak = deltas.Max(v => v.sqrMagnitude);
                    if (peak <= 1e-14f) continue;
                    var region = Enumerable.Range(0, points.Length)
                        .Where(i => deltas[i].sqrMagnitude >= peak * .22f * .22f).ToArray();
                    if (region.Length >= 8) regions.Add(region);
                }

                if (regions.Count < 2)
                    throw new InvalidOperationException("有効な Viseme の変位領域が2種類未満です。現在位置を維持します。");
                var consensus = Median(regions.Select(r => Median(r.Select(i => points[i]))));
                var accepted = regions.Where(r => Vector3.Distance(Median(r.Select(i => points[i])), consensus) <= .045f * scale).ToArray();
                var votes = new int[points.Length];
                foreach (var region in accepted)
                    foreach (int index in region) votes[index]++;
                var shared = Enumerable.Range(0, points.Length).Where(i => votes[i] >= 2).ToArray();
                if (shared.Length < 8)
                    throw new InvalidOperationException("複数の Viseme で一致する口元領域が不足しています。");

                var middle = Median(shared.Select(i => points[i]));
                var width = Mathf.Max(.004f * scale, shared.Select(i => Mathf.Abs(points[i].x - middle.x))
                    .OrderBy(x => x).ElementAt(shared.Length * 3 / 4));
                var triangles = baked.triangles;
                Vector3[] FindFront(Vector3[] normalsToUse) => shared.Where(i =>
                    normalsToUse[i].z > .15f && Mathf.Abs(points[i].x - middle.x) < width)
                    .Select(i => points[i]).ToArray();
                var front = FindFront(normals);
                bool rebuiltNormals = front.Length < 5;
                if (rebuiltNormals)
                {
                    normals = SurfaceNormals(points, triangles);
                    front = FindFront(normals);
                }
                if (front.Length < 5)
                    throw new InvalidOperationException("顔の正面を特定できません。");
                var mouth = Median(front);
                var relative = mouth - frame.inverse.MultiplyPoint3x4(head.position);
                if (relative.magnitude > .20f * scale || relative.z < -.035f * scale)
                    throw new InvalidOperationException("口元候補が Head 付近にありません。ボーン、正面方向、スケールを確認してください。");

                bool projected = Project(mouth, points, normals, triangles, .015f * scale, out var surface);
                var strip = Enumerable.Range(0, points.Length)
                    .Where(i => Mathf.Abs(points[i].x - mouth.x) < width * .4f &&
                        points[i].y > mouth.y + width * .25f && points[i].y < mouth.y + width * 1.8f &&
                        normals[i].z > .15f)
                    .OrderByDescending(i => points[i].z).Take(5).Select(i => points[i]).ToArray();
                var nose = strip.Length >= 3 ? Median(strip) : surface + Vector3.up * width * .5f;
                return new Result
                {
                    Mouth = frame.MultiplyPoint3x4(mouth),
                    Surface = frame.MultiplyPoint3x4(surface),
                    Nose = frame.MultiplyPoint3x4(nose),
                    Midpoint = frame.MultiplyPoint3x4((surface + nose) * .5f),
                    ReliableNose = strip.Length >= 3 && nose.z > surface.z + .001f * scale,
                    Projected = projected,
                    RebuiltNormals = rebuiltNormals,
                    Visemes = accepted.Length
                };
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }

        internal static float CheckFrame(Transform target)
        {
            var m = target.localToWorldMatrix;
            var x = m.MultiplyVector(Vector3.right);
            var y = m.MultiplyVector(Vector3.up);
            var z = m.MultiplyVector(Vector3.forward);
            float s = x.magnitude;
            if (s < .00001f || !float.IsFinite(s) || Mathf.Abs(y.magnitude / s - 1) > .015f ||
                Mathf.Abs(z.magnitude / s - 1) > .015f ||
                Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > .01f ||
                Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > .01f ||
                Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > .01f ||
                Vector3.Dot(x.normalized, target.right) < .999f ||
                Vector3.Dot(y.normalized, target.up) < .999f ||
                Vector3.Dot(z.normalized, target.forward) < .999f)
                throw new InvalidOperationException("負、非一様、またはせん断を含むスケールには対応していません。");
            return s;
        }

        private static Vector3 Median(IEnumerable<Vector3> input)
        {
            var points = input.ToArray();
            float Axis(Func<Vector3, float> get) => points.Select(get).OrderBy(x => x).ElementAt(points.Length / 2);
            return new Vector3(Axis(v => v.x), Axis(v => v.y), Axis(v => v.z));
        }

        private static Vector3[] SurfaceNormals(Vector3[] points, int[] triangles)
        {
            var normals = new Vector3[points.Length];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                // Unity の正面は時計回り。
                var normal = Vector3.Cross(points[c] - points[a], points[b] - points[a]);
                normals[a] += normal;
                normals[b] += normal;
                normals[c] += normal;
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = normals[i].normalized;
            return normals;
        }

        private static bool Project(Vector3 point, Vector3[] points, Vector3[] normals, int[] triangles, float max, out Vector3 result)
        {
            result = point;
            bool found = false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = points[triangles[i]];
                var b = points[triangles[i + 1]] - a;
                var c = points[triangles[i + 2]] - a;
                var p = point - a;
                var n = Vector3.Cross(b, c);
                if (Mathf.Abs(n.z) <= 1e-10f) continue;
                float u = (p.x * c.y - p.y * c.x) / n.z;
                float v = (b.x * p.y - b.y * p.x) / n.z;
                if (u < 0 || v < 0 || u + v > 1) continue;
                var normal = normals[triangles[i]] * (1 - u - v) +
                    normals[triangles[i + 1]] * u + normals[triangles[i + 2]] * v;
                if (normal.normalized.z < .15f) continue;
                float z = a.z + u * b.z + v * c.z;
                if (z < result.z || z - point.z > max) continue;
                result.z = z;
                found = true;
            }
            return found;
        }
    }
}
