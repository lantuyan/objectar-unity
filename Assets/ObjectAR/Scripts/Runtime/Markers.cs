using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace ObjectAR
{
    /// Một model đặt trên bảng marker. Toạ độ (mét) trong hệ của mã QR (quy ước ARKit của bản Swift):
    /// gốc = tâm mã QR, x → phải, z → cạnh dưới mã, y → vuông góc mặt mã (hướng lên). yaw = độ.
    /// Khai báo trường theo thứ tự abc = thứ tự khoá của JSONEncoder .sortedKeys bên Swift.
    [Serializable]
    public class Placement
    {
        public string id = NewID();
        public string modelID;
        public float scale = 0.01f;
        public float x;
        public float y;
        public float yaw;
        public float z;

        public Placement Clone() => (Placement)MemberwiseClone();

        /// UUID dạng chữ hoa như Foundation.UUID.uuidString.
        public static string NewID() => Guid.NewGuid().ToString().ToUpperInvariant();
    }

    /// Bảng marker: 1 mã QR + danh sách model hiển thị quanh mã.
    [Serializable]
    public class MarkerBoard
    {
        public string id;
        public string name;
        public string payload;                          // nội dung mã QR
        public List<Placement> placements = new();
        public float widthCm = 4.5f;                    // cạnh thật của ô QR (không tính viền trắng)

        public MarkerBoard Clone()
        {
            var b = (MarkerBoard)MemberwiseClone();
            b.placements = placements.ConvertAll(p => p.Clone());
            return b;
        }
    }

    public static class MarkerStore
    {
        const string KnownKey = "markers.knownDefaults";
        static string FilePath => Path.Combine(Application.persistentDataPath, "markers.json");

        public static List<MarkerBoard> Load()
        {
            var defs = Defaults();
            try
            {
                var b = File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : null;
                if (b == null || b.Count == 0) return defs;
                // Thêm bảng mặc định mới (có ở bản app này nhưng người dùng chưa từng thấy), không hồi sinh bảng đã xoá.
                var known = new HashSet<string>(PlayerPrefs.GetString(KnownKey, "M1,M2,M3,M4").Split(','));
                foreach (var nb in defs)
                    if (!known.Contains(nb.id) && !b.Exists(x => x.id == nb.id || x.payload == nb.payload)) b.Add(nb);
                Save(b);
                return b;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ObjectAR] Không đọc được markers.json: {e.Message}");
                return defs;
            }
            finally
            {
                PlayerPrefs.SetString(KnownKey, string.Join(",", defs.Select(d => d.id)));
                PlayerPrefs.Save();
            }
        }

        public static void Save(List<MarkerBoard> boards)
        {
            try
            {
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, ToJson(boards));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e) { Debug.LogWarning($"[ObjectAR] Không ghi được markers.json: {e.Message}"); }
        }

        /// Mảng JSON cùng định dạng bản Swift (khoá id,name,payload,placements,widthCm).
        public static string ToJson(List<MarkerBoard> boards) => JsonConvert.SerializeObject(boards, Formatting.Indented);
        public static List<MarkerBoard> Parse(string json) => JsonConvert.DeserializeObject<List<MarkerBoard>>(json);

        static Placement P(string model, float x, float z, float scale) => new() { modelID = model, x = x, z = z, scale = scale };

        /// Bộ bảng mặc định — trùng với các file trong thư mục QR-markers/ (bản mới mỗi lần gọi).
        public static List<MarkerBoard> Defaults() => new()
        {
            new() { id = "M1", name = "Khu dân cư", payload = "OBJECTAR:M1:khu-dan-cu", placements = new()
            {
                P("house-pho", -0.075f, 0, 0.008f), P("house-cap4", 0.085f, 0, 0.008f), P("tree", 0, -0.06f, 0.008f),
            } },
            new() { id = "M2", name = "Biệt thự sân vườn", payload = "OBJECTAR:M2:biet-thu", placements = new()
            {
                P("house-bietthu", 0.11f, 0, 0.006f),
            } },
            new() { id = "M3", name = "Chung cư", payload = "OBJECTAR:M3:chung-cu", placements = new()
            {
                P("house-chungcu", 0.1f, 0, 0.004f), P("tree", 0.035f, 0.055f, 0.006f), P("tree", 0.165f, 0.055f, 0.006f),
            } },
            new() { id = "M4", name = "Bảng trống", payload = "OBJECTAR:M4:bang-trong" },
            new() { id = "M5", name = "Laptop Acer Nitro 5", payload = "OBJECTAR:M5:acer-nitro5", placements = new()
            {
                P("acer-nitro5", 0.12f, 0, 0.3f),
            } },
            new() { id = "M6", name = "Toà nhà low-poly", payload = "OBJECTAR:M6:low-poly-building", placements = new()
            {
                P("low-poly-building", 0.1f, 0, 0.015f),
            } },
        };
    }
}
