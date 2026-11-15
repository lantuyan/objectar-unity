using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ObjectAR
{
    /// Mô hình nhà có sẵn trong app (Resources/Models/*.glb, đơn vị mét, mặt tiền hướng +z hệ dữ liệu).
    public sealed class HouseModel
    {
        public string Id { get; }
        public string Name { get; }
        public string File { get; }
        public string Icon { get; }
        public float DefaultScale { get; }   // tỉ lệ mặc định khi đặt lên bảng (0.01 = 1:100)
        public string SpecID { get; }        // dùng ModelInstance + ModelSpecs (vd laptop: đơn vị mm, nắp, màn hình)
        public float FrontYaw { get; }       // rad, xoay để mặt trước hướng +z

        public HouseModel(string id, string name, string file, string icon, float defaultScale,
                          string specID = null, float frontYaw = 0)
        {
            Id = id; Name = name; File = file; Icon = icon; DefaultScale = defaultScale; SpecID = specID; FrontYaw = frontYaw;
        }
    }

    public static class HouseCatalog
    {
        public static IReadOnlyList<HouseModel> All { get; } = new[]
        {
            new HouseModel("house-cap4", "Nhà cấp 4", "house-cap4.glb", "🏠", 0.008f),
            new HouseModel("house-pho", "Nhà phố 4 tầng", "house-pho.glb", "🏢", 0.008f),
            new HouseModel("house-bietthu", "Biệt thự sân vườn", "house-bietthu.glb", "🏡", 0.006f),
            new HouseModel("house-chungcu", "Chung cư 10 tầng", "house-chungcu.glb", "🏬", 0.004f),
            new HouseModel("tree", "Cây xanh", "tree.glb", "🌳", 0.008f),
            new HouseModel("low-poly-building", "Toà nhà low-poly", "low-poly-building.glb", "🏛", 0.015f),
            new HouseModel("acer-nitro5", "Laptop Acer Nitro 5", "AcerNitro5.glb", "💻", 0.3f, specID: "acer-nitro5-an515-57"),
        };

        public static HouseModel Model(string id) => All.FirstOrDefault(m => m.Id == id);

        /// Node model đã chuẩn hoá: đơn vị mét, gốc = tâm đáy, mặt trước hướng +z (hệ dữ liệu). null nếu lỗi.
        /// Cây: outer → orient (Euler 0, 180 - frontYaw) → content (bản glTF dời tâm-đáy về gốc).
        public static GameObject MakeNode(string id)
        {
            var m = Model(id);
            if (m == null) return null;
            if (m.SpecID != null && ModelSpecs.All.TryGetValue(m.SpecID, out var spec))
            {
                var st = ModelLibrary.Template(spec.File);
                return st != null ? ModelInstance.Create(st, spec).gameObject : null;
            }
            var t = ModelLibrary.Template(m.File);
            if (t == null) return null;
            var outer = new GameObject(m.Id);
            var orient = NodeUtil.Child(outer.transform, "orient");
            orient.localRotation = Quaternion.Euler(0, 180f - m.FrontYaw * Mathf.Rad2Deg, 0);
            var content = Object.Instantiate(t, orient, false).transform;
            var b = NodeUtil.BoundsIn(orient, content.gameObject);
            content.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            return outer;
        }
    }
}
