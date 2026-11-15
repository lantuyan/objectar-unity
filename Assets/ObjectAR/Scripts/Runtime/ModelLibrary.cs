using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ObjectAR
{
    /// Nạp & cache prefab GLB (glTFast import lúc editor) từ Resources/Models/.
    public static class ModelLibrary
    {
        static readonly Dictionary<string, GameObject> cache = new();

        static string ResPath(string file) => "Models/" + Path.GetFileNameWithoutExtension(file);

        /// Prefab gốc (không instantiate). null nếu không có file.
        public static GameObject Template(string file)
        {
            if (cache.TryGetValue(file, out var t)) return t;
            t = Resources.Load<GameObject>(ResPath(file));
            if (t == null) Debug.LogWarning($"[ObjectAR] Không tìm thấy model {file}");
            else cache[file] = t;
            return t;
        }

        /// Nạp trước mọi model trong catalog (bất đồng bộ) để lần nhận mã đầu không giật.
        public static IEnumerator Preload()
        {
            foreach (var file in HouseCatalog.All.Select(m => m.File).Distinct())
            {
                if (cache.ContainsKey(file)) continue;
                var rq = Resources.LoadAsync<GameObject>(ResPath(file));
                yield return rq;
                if (rq.asset is GameObject g) cache[file] = g;
            }
        }
    }
}
