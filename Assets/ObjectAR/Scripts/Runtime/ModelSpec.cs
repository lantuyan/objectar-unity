using System.Collections.Generic;

namespace ObjectAR
{
    /// Vùng tấm màn hình trong toạ độ node nắp (đơn vị file, hệ glTF). Mặt màn hình nhìn về -y.
    public sealed class ScreenSpec
    {
        public readonly float X0, X1, Z0, Z1, Y;
        public ScreenSpec(float x0, float x1, float z0, float z1, float y) { X0 = x0; X1 = x1; Z0 = z0; Z1 = z1; Y = y; }
    }

    public sealed class ModelSpec
    {
        public readonly string File;        // file trong Resources/Models/
        public readonly float UnitScale;    // đơn vị file → mét (mm = 0.001)
        public readonly string BaseNode;    // node dùng để căn tâm/đáy (thân máy)
        public readonly string LidNode;     // node bản lề (màn hình laptop)
        public readonly float LidDefault;   // góc mở mặc định (độ)
        public readonly float FrontYaw;     // rad, xoay để mặt trước model quay về +z (hệ dữ liệu)
        public readonly ScreenSpec Screen;

        public ModelSpec(string file, float unitScale, string baseNode, string lidNode, float lidDefault, float frontYaw,
                         ScreenSpec screen = null)
        {
            File = file; UnitScale = unitScale; BaseNode = baseNode; LidNode = lidNode;
            LidDefault = lidDefault; FrontYaw = frontYaw; Screen = screen;
        }
    }

    /// Model GLB cần căn chỉnh riêng (đơn vị, bản lề, màn hình) khi đặt lên bảng QR.
    public static class ModelSpecs
    {
        public static readonly IReadOnlyDictionary<string, ModelSpec> All = new Dictionary<string, ModelSpec>
        {
            ["acer-nitro5-an515-57"] = new ModelSpec("AcerNitro5.glb", 0.001f, "Main Body", "Monitor Body", 120f, -UnityEngine.Mathf.PI / 2,
                                                     new ScreenSpec(38.4f, 241.6f, -173.9f, 173.9f, 1.8f)),
        };
    }
}
