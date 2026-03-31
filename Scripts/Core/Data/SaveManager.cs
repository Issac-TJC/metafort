using Godot;
using System.IO;

namespace MetaFort.Core.Data
{
    /// <summary>
    /// 全局二进制极速存档管理器
    /// 负责安全且无锁地执行 3 槽位的本地写入与拉取
    /// </summary>
    public static class SaveManager
    {
        // 自动定位系统用户的 AppData / 本地保存目录
        public static string GetSavePath(int slot, int subSlot) => ProjectSettings.GlobalizePath($"user://metafort_save_slot_{slot}_sub_{subSlot}.dat");

        public static bool SaveExists(int slot, int subSlot)
        {
            return File.Exists(GetSavePath(slot, subSlot));
        }

        public static bool HasAnySubSave(int slot)
        {
            for (int i = 0; i <= 9; i++)
            {
                if (SaveExists(slot, i)) return true;
            }
            return false;
        }

        public static void SaveGame(int slot, int subSlot, int seed, int w, int h, int d, byte[] mapBytes)
        {
            string path = GetSavePath(slot, subSlot);
            
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (var writer = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                writer.Write("METAFORT");  // Magic Header 防护
                writer.Write(seed);
                writer.Write(w);
                writer.Write(h);
                writer.Write(d);
                writer.Write(mapBytes.Length);
                writer.Write(mapBytes); // O(1) 原生内存灌注法
            }
        }

        public static bool LoadGame(int slot, int subSlot, out int seed, out int w, out int h, out int d, out byte[] mapBytes)
        {
            seed = w = h = d = 0;
            mapBytes = null;
            string path = GetSavePath(slot, subSlot);
            if (!File.Exists(path)) return false;

            using (var reader = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                if (reader.ReadString() != "METAFORT") return false; // 校验损坏的魔数
                seed = reader.ReadInt32();
                w = reader.ReadInt32();
                h = reader.ReadInt32();
                d = reader.ReadInt32();
                int length = reader.ReadInt32();
                mapBytes = reader.ReadBytes(length);
                return true;
            }
        }

        public static void SaveVillagers(int slot, int subSlot, string jsonContent)
        {
            string path = ProjectSettings.GlobalizePath($"user://metafort_save_slot_{slot}_sub_{subSlot}_villagers.json");
            File.WriteAllText(path, jsonContent);
        }

        public static bool LoadVillagers(int slot, int subSlot, out string jsonContent)
        {
            jsonContent = null;
            string path = ProjectSettings.GlobalizePath($"user://metafort_save_slot_{slot}_sub_{subSlot}_villagers.json");
            if (!File.Exists(path)) return false;

            jsonContent = File.ReadAllText(path);
            return true;
        }

        public static void DeleteSave(int slot)
        {
            // 一并引爆主宇宙底下的全部微观物理时间线 (0-9子档)
            for (int sub = 0; sub <= 9; sub++)
            {
                string path = GetSavePath(slot, sub);
                if (File.Exists(path)) File.Delete(path);

                string villagerPath = ProjectSettings.GlobalizePath($"user://metafort_save_slot_{slot}_sub_{sub}_villagers.json");
                if (File.Exists(villagerPath)) File.Delete(villagerPath);
            }
        }
    }
}
