// 把这个脚本放进 Assets/Editor/ 文件夹
// Unity 顶部菜单 → Tools → Generate 3D Noise Texture (V2)
//
// 相比 V1 的改进:
//   - Worley 用 F2-F1 距离差(消除网格感的标准做法)
//   - 每个通道叠加多倍频(2 层),进一步打散规则性
//   - 可选 Domain Warp:用 value 噪声扰动采样位置,网格直接被掰弯
//   - jitter 范围可调

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public class Noise3DGeneratorV2 : EditorWindow
{
    int resolution = 64;
    int gridR = 4;
    int gridG = 8;
    int gridB = 16;
    int valueOctaves = 4;
    float jitterAmount = 1.0f;          // 1.0 = 满格抖动,>1 会让点跨格子(更乱)
    float warpStrength = 0.15f;         // 0 = 关掉,典型 0.1~0.3
    bool useF2MinusF1 = true;           // 关键:消除网格感
    int seed = 12345;
    string savePath = "Assets/Noise3D.asset";

    [MenuItem("Tools/Generate 3D Noise Texture (V2)")]
    public static void Open()
    {
        var w = GetWindow<Noise3DGeneratorV2>("3D Noise V2");
        w.minSize = new Vector2(380, 320);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("RGB = Worley 三个频率 / A = Value FBM", EditorStyles.miniLabel);
        EditorGUILayout.Space();

        resolution = EditorGUILayout.IntPopup("Resolution", resolution,
            new[] { "32 (fast)", "64 (recommended)", "128 (heavy, 8MB)" },
            new[] { 32, 64, 128 });

        EditorGUILayout.LabelField("Worley Grids", EditorStyles.boldLabel);
        gridR = Mathf.Clamp(EditorGUILayout.IntField("R - Big",    gridR), 2, 32);
        gridG = Mathf.Clamp(EditorGUILayout.IntField("G - Medium", gridG), 2, 32);
        gridB = Mathf.Clamp(EditorGUILayout.IntField("B - Small",  gridB), 2, 32);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Anti-grid Tricks", EditorStyles.boldLabel);
        useF2MinusF1 = EditorGUILayout.Toggle(
            new GUIContent("Use F2 - F1 distance",
                           "用第二近距离减第一近距离,消除规则网格感"),
            useF2MinusF1);
        jitterAmount = EditorGUILayout.Slider(
            new GUIContent("Jitter Amount",
                           "1=满格,>1 让点跨进相邻格子,分布更乱"),
            jitterAmount, 0.5f, 1.5f);
        warpStrength = EditorGUILayout.Slider(
            new GUIContent("Domain Warp",
                           "用 value 噪声扰动采样位置,网格被掰弯。0=关闭"),
            warpStrength, 0f, 0.5f);

        EditorGUILayout.Space();
        valueOctaves = Mathf.Clamp(EditorGUILayout.IntField("A - Value FBM octaves", valueOctaves), 1, 6);
        seed = EditorGUILayout.IntField("Seed", seed);
        savePath = EditorGUILayout.TextField("Save Path", savePath);

        EditorGUILayout.Space();
        GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
        if (GUILayout.Button("Generate", GUILayout.Height(34))) Generate();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.HelpBox(
            "如果还能看到明显条带:\n" +
            " • 把 Domain Warp 拉到 0.2~0.3\n" +
            " • 把 grid 数都设成奇数(5/9/15 而不是 4/8/16)\n" +
            " • 换 seed",
            MessageType.Info);
    }

    void Generate()
    {
        Random.InitState(seed);

        // 每个通道叠两层 grid,频率各错开 1.5 倍
        var ptsR1 = MakeJitteredGrid(gridR);
        var ptsR2 = MakeJitteredGrid(Mathf.Max(2, Mathf.RoundToInt(gridR * 1.7f)));
        var ptsG1 = MakeJitteredGrid(gridG);
        var ptsG2 = MakeJitteredGrid(Mathf.Max(2, Mathf.RoundToInt(gridG * 1.7f)));
        var ptsB1 = MakeJitteredGrid(gridB);
        var ptsB2 = MakeJitteredGrid(Mathf.Max(2, Mathf.RoundToInt(gridB * 1.7f)));

        var dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tex = new Texture3D(resolution, resolution, resolution, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 0;

        var pixels = new Color[resolution * resolution * resolution];
        int idx = 0;

        for (int z = 0; z < resolution; z++)
        {
            if (z % 4 == 0)
                EditorUtility.DisplayProgressBar("Generating 3D Noise V2",
                    $"Slice {z}/{resolution}", (float)z / resolution);

            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                Vector3 p = new Vector3(x, y, z) / resolution;

                // Domain warp:用 value 噪声扰动采样位置(可平铺)
                if (warpStrength > 0)
                {
                    Vector3 warp = new Vector3(
                        TileableValue(p + new Vector3(0.31f, 0.07f, 0.59f), 4),
                        TileableValue(p + new Vector3(0.83f, 0.41f, 0.13f), 4),
                        TileableValue(p + new Vector3(0.17f, 0.97f, 0.71f), 4)
                    ) - new Vector3(0.5f, 0.5f, 0.5f);
                    p = WrapVec3(p + warp * warpStrength);
                }

                // 三个 Worley 通道,每个叠两层 + F2-F1
                float r = WorleyLayered(p, ptsR1, gridR, ptsR2, Mathf.Max(2, Mathf.RoundToInt(gridR * 1.7f)));
                float g = WorleyLayered(p, ptsG1, gridG, ptsG2, Mathf.Max(2, Mathf.RoundToInt(gridG * 1.7f)));
                float b = WorleyLayered(p, ptsB1, gridB, ptsB2, Mathf.Max(2, Mathf.RoundToInt(gridB * 1.7f)));
                float a = ValueFBM(p, valueOctaves);

                pixels[idx++] = new Color(r, g, b, a);
            }
        }
        EditorUtility.ClearProgressBar();

        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false);

        var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(savePath);
        if (existing != null)
            EditorUtility.CopySerialized(tex, existing);
        else
            AssetDatabase.CreateAsset(tex, savePath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var saved = AssetDatabase.LoadAssetAtPath<Texture3D>(savePath);
        Selection.activeObject = saved;
        EditorGUIUtility.PingObject(saved);

        Debug.Log($"[Noise3DGenerator V2] {resolution}³ → {savePath}  " +
                  $"({resolution * resolution * resolution * 4 / 1024} KB)");
    }

    Vector3 WrapVec3(Vector3 v)
    {
        v.x -= Mathf.Floor(v.x);
        v.y -= Mathf.Floor(v.y);
        v.z -= Mathf.Floor(v.z);
        return v;
    }

    // ---- Worley(支持 F2-F1 和 jitter 强度) ----
    Vector3[,,] MakeJitteredGrid(int gridDim)
    {
        var pts = new Vector3[gridDim, gridDim, gridDim];
        float step = 1f / gridDim;
        for (int z = 0; z < gridDim; z++)
        for (int y = 0; y < gridDim; y++)
        for (int x = 0; x < gridDim; x++)
        {
            var cell = new Vector3(x, y, z) * step;
            // jitterAmount > 1 时点会跑出格子,但因为搜索 3x3x3 邻居+取模,仍然连续可平铺
            var jitter = new Vector3(
                (Random.value - 0.5f) * jitterAmount + 0.5f,
                (Random.value - 0.5f) * jitterAmount + 0.5f,
                (Random.value - 0.5f) * jitterAmount + 0.5f) * step;
            pts[x, y, z] = cell + jitter;
        }
        return pts;
    }

    // 一层 Worley:返回 (F1, F2)
    Vector2 WorleyF1F2(Vector3 p, Vector3[,,] pts, int gridDim)
    {
        int cx = Mathf.FloorToInt(p.x * gridDim);
        int cy = Mathf.FloorToInt(p.y * gridDim);
        int cz = Mathf.FloorToInt(p.z * gridDim);

        float f1 = float.MaxValue, f2 = float.MaxValue;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = (cx + dx + gridDim) % gridDim;
            int ny = (cy + dy + gridDim) % gridDim;
            int nz = (cz + dz + gridDim) % gridDim;

            var pt = pts[nx, ny, nz];
            if (cx + dx < 0)             pt.x -= 1f;
            else if (cx + dx >= gridDim) pt.x += 1f;
            if (cy + dy < 0)             pt.y -= 1f;
            else if (cy + dy >= gridDim) pt.y += 1f;
            if (cz + dz < 0)             pt.z -= 1f;
            else if (cz + dz >= gridDim) pt.z += 1f;

            float distSq = (p - pt).sqrMagnitude;
            if (distSq < f1) { f2 = f1; f1 = distSq; }
            else if (distSq < f2) { f2 = distSq; }
        }
        return new Vector2(Mathf.Sqrt(f1) * gridDim, Mathf.Sqrt(f2) * gridDim);
    }

    // 两层叠加 + F2-F1,输出 [0,1]
    float WorleyLayered(Vector3 p, Vector3[,,] ptsA, int gridA,
                                      Vector3[,,] ptsB, int gridB)
    {
        Vector2 a = WorleyF1F2(p, ptsA, gridA);
        Vector2 b = WorleyF1F2(p, ptsB, gridB);

        float wa = useF2MinusF1
            ? Mathf.Clamp01(a.y - a.x)            // F2-F1:细胞中心远离边界
            : Mathf.Clamp01(1f - a.x);            // 普通反相
        float wb = useF2MinusF1
            ? Mathf.Clamp01(b.y - b.x)
            : Mathf.Clamp01(1f - b.x);

        // 叠加:大尺度为主,小尺度做细节扰动
        return Mathf.Clamp01(wa * 0.7f + wb * 0.3f);
    }

    // ---- 可平铺 Value FBM ----
    float ValueFBM(Vector3 p, int octaves)
    {
        float v = 0, amp = 0.5f;
        int freq = 4;
        for (int i = 0; i < octaves; i++)
        {
            v += amp * TileableValue(p, freq);
            amp *= 0.5f;
            freq *= 2;
        }
        return Mathf.Clamp01(v);
    }

    float TileableValue(Vector3 p, int freq)
    {
        Vector3 pf = p * freq;
        Vector3 i = new Vector3(Mathf.Floor(pf.x), Mathf.Floor(pf.y), Mathf.Floor(pf.z));
        Vector3 f = pf - i;
        f = new Vector3(f.x*f.x*(3-2*f.x), f.y*f.y*(3-2*f.y), f.z*f.z*(3-2*f.z));

        int x0 = ((int)i.x % freq + freq) % freq, x1 = (x0 + 1) % freq;
        int y0 = ((int)i.y % freq + freq) % freq, y1 = (y0 + 1) % freq;
        int z0 = ((int)i.z % freq + freq) % freq, z1 = (z0 + 1) % freq;

        float n000 = HashInt(x0, y0, z0), n100 = HashInt(x1, y0, z0);
        float n010 = HashInt(x0, y1, z0), n110 = HashInt(x1, y1, z0);
        float n001 = HashInt(x0, y0, z1), n101 = HashInt(x1, y0, z1);
        float n011 = HashInt(x0, y1, z1), n111 = HashInt(x1, y1, z1);

        return Mathf.Lerp(
            Mathf.Lerp(Mathf.Lerp(n000, n100, f.x), Mathf.Lerp(n010, n110, f.x), f.y),
            Mathf.Lerp(Mathf.Lerp(n001, n101, f.x), Mathf.Lerp(n011, n111, f.x), f.y),
            f.z);
    }

    float HashInt(int x, int y, int z)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + z * 1442695040 + seed * 17;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }
    }
}
#endif