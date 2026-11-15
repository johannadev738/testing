using System.Collections.Generic;
using UnityEngine;

namespace SimSafe
{
    /// <summary>
    /// Material and primitive helpers for the procedural environment.
    /// Materials are cloned from a base material saved by the SimSafe setup menu
    /// (Resources/SimSafe/BaseLit.mat) so the right shader and variants are included in builds.
    /// In the editor, if that asset is missing, it falls back to Shader.Find.
    /// </summary>
    public static class Gfx
    {
        static Material litBase, unlitBase;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Font font;

        public static Font BuiltinFont
        {
            get { if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); return font; }
        }

        static Shader FindAny(params string[] names)
        {
            foreach (var n in names) { var s = Shader.Find(n); if (s != null) return s; }
            return Shader.Find("Sprites/Default");
        }

        static Material LitBase()
        {
            if (litBase != null) return litBase;
            litBase = Resources.Load<Material>("SimSafe/BaseLit");
            if (litBase == null) litBase = new Material(FindAny("Universal Render Pipeline/Lit", "Standard"));
            return litBase;
        }

        static Material UnlitBase()
        {
            if (unlitBase != null) return unlitBase;
            unlitBase = Resources.Load<Material>("SimSafe/BaseUnlit");
            if (unlitBase == null) unlitBase = new Material(FindAny("Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default"));
            return unlitBase;
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static Material Lit(Color c, float smoothness = 0.15f)
        {
            string key = "L" + ColorUtility.ToHtmlStringRGBA(c) + smoothness.ToString("0.00");
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(LitBase());
            SetColor(m, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            cache[key] = m;
            return m;
        }

        public static Material Unlit(Color c)
        {
            string key = "U" + ColorUtility.ToHtmlStringRGBA(c);
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(UnlitBase());
            SetColor(m, c);
            cache[key] = m;
            return m;
        }

        public static Color Hex(string html)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(html, out c) ? c : Color.magenta;
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos,
            Vector3 localScale, Material mat, bool collider = true, Vector3? euler = null)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = localScale;
            if (euler.HasValue) g.transform.localEulerAngles = euler.Value;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.Destroy(g.GetComponent<Collider>());
            return g;
        }

        public static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider = true)
        {
            return Prim(PrimitiveType.Cube, parent, name, pos, size, mat, collider);
        }

        /// <summary>Cylinder with real-world radius/height (Unity's cylinder is 2m tall, 1m wide by default).</summary>
        public static GameObject Cyl(Transform parent, string name, Vector3 pos, float radius, float height, Material mat, bool collider = false, Vector3? euler = null)
        {
            return Prim(PrimitiveType.Cylinder, parent, name, pos, new Vector3(radius * 2f, height * 0.5f, radius * 2f), mat, collider, euler);
        }

        public static GameObject Ball(Transform parent, string name, Vector3 pos, float diameter, Material mat, bool collider = false)
        {
            return Prim(PrimitiveType.Sphere, parent, name, pos, Vector3.one * diameter, mat, collider);
        }

        /// <summary>Gable roof prism: ridge runs along X. Each face has its own vertices for flat shading.</summary>
        public static Mesh GableRoof(float width, float depth, float rise)
        {
            float hw = width * 0.5f, hd = depth * 0.5f;
            Vector3 A = new Vector3(-hw, 0, -hd), B = new Vector3(hw, 0, -hd), C = new Vector3(hw, 0, hd), D = new Vector3(-hw, 0, hd);
            Vector3 R1 = new Vector3(-hw, rise, 0), R2 = new Vector3(hw, rise, 0);
            var v = new List<Vector3>(); var t = new List<int>();
            System.Action<Vector3, Vector3, Vector3> tri = (a, b, c) => { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2); };
            tri(A, R1, R2); tri(A, R2, B);      // front slope (faces -Z)
            tri(C, R2, R1); tri(C, R1, D);      // back slope (faces +Z)
            tri(B, R2, C); tri(D, R1, A);       // gable ends (+X, -X)
            var m = new Mesh { name = "GableRoof" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
