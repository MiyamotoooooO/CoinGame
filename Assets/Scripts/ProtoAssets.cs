using System.Collections.Generic;
using UnityEngine;

namespace MedalPusher
{
    /// <summary>
    /// 試作用のメッシュ・マテリアルをコードで生成する。
    /// Built-in / URP どちらのプロジェクトでも動くよう、プリミティブの既定マテリアルを複製して使う。
    /// </summary>
    public static class ProtoAssets
    {
        static Material baseMaterial;

        public static Material MakeMaterial(Color color, float metallic = 0f, float smoothness = 0.4f)
        {
            if (baseMaterial == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseMaterial = temp.GetComponent<Renderer>().sharedMaterial;
                // Destroy だとそのフレームの物理に原点の箱が残るため Immediate で消す
                UnityEngine.Object.DestroyImmediate(temp);
            }

            var m = new Material(baseMaterial);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);   // URP
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness); // Built-in
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness); // URP
            return m;
        }

        /// <summary>Y軸を中心にした円柱（メダル）。中心が原点。</summary>
        public static Mesh CreateCylinderMesh(float radius, float height, int segments)
        {
            segments = Mathf.Max(3, segments);
            float h = height * 0.5f;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();

            // 上面
            int top = verts.Count;
            verts.Add(new Vector3(0f, h, 0f)); normals.Add(Vector3.up);
            for (int i = 0; i < segments; i++)
            {
                verts.Add(Ring(i, segments, radius, h)); normals.Add(Vector3.up);
            }
            for (int i = 0; i < segments; i++)
            {
                tris.Add(top); tris.Add(top + 1 + (i + 1) % segments); tris.Add(top + 1 + i);
            }

            // 下面
            int bottom = verts.Count;
            verts.Add(new Vector3(0f, -h, 0f)); normals.Add(Vector3.down);
            for (int i = 0; i < segments; i++)
            {
                verts.Add(Ring(i, segments, radius, -h)); normals.Add(Vector3.down);
            }
            for (int i = 0; i < segments; i++)
            {
                tris.Add(bottom); tris.Add(bottom + 1 + i); tris.Add(bottom + 1 + (i + 1) % segments);
            }

            // 側面（上下の頂点を交互に並べる）
            int side = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                Vector3 pTop = Ring(i, segments, radius, h);
                Vector3 n = new Vector3(pTop.x, 0f, pTop.z).normalized;
                verts.Add(pTop); normals.Add(n);                                   // side + i*2
                verts.Add(Ring(i, segments, radius, -h)); normals.Add(n);          // side + i*2 + 1
            }
            for (int i = 0; i < segments; i++)
            {
                int t0 = side + i * 2, b0 = t0 + 1;
                int t1 = side + ((i + 1) % segments) * 2, b1 = t1 + 1;
                tris.Add(b0); tris.Add(t0); tris.Add(t1);
                tris.Add(b0); tris.Add(t1); tris.Add(b1);
            }

            var mesh = new Mesh { name = $"Cylinder_{segments}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Vector3 Ring(int i, int segments, float r, float y)
        {
            float a = i * Mathf.PI * 2f / segments;
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }
    }
}
