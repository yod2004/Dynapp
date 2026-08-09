using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Dynapp
{
    // 制御モードの全設定を保存/復元するためのDTO(プレーンなデータ入れ物)。
    // ViewModelはINotifyPropertyChanged等を持つのでシリアライズ用に別途この型へ写し取る。
    public class ControlPreset
    {
        public string Name { get; set; } = "";
        public double ControlCurrentLimit { get; set; } = 300;
        public int ControlLoopDelayMs { get; set; } = 10;

        public List<InputDto> Inputs { get; set; } = new();
        public List<StateDto> States { get; set; } = new();
        public List<DerivativeDto> Derivatives { get; set; } = new();
        public List<TableDto> Tables { get; set; } = new();
        public List<ExprDto> Defines { get; set; } = new();
        public List<ExprDto> Monitors { get; set; } = new();
        public List<ControlLawDto> ControlLaws { get; set; } = new();
    }

    public class TableDto
    {
        public string Name { get; set; } = "";
        public string XExpression { get; set; } = "";
        public string FileName { get; set; } = "";
        public double[] Xs { get; set; } = Array.Empty<double>();
        public double[] Ys { get; set; } = Array.Empty<double>();
    }

    public class InputDto
    {
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public double Min { get; set; } = -100;
        public double Max { get; set; } = 100;
        public int Steps { get; set; } = 100;
    }

    public class StateDto
    {
        public string Name { get; set; } = "";
        public string Integrand { get; set; } = "";
        public double Limit { get; set; } = 100000;
    }

    public class DerivativeDto
    {
        public string Name { get; set; } = "";
        public string Expression { get; set; } = "";
        public double Tau { get; set; } = 0.02;
    }

    public class ExprDto
    {
        public string Name { get; set; } = "";
        public string Expression { get; set; } = "";
    }

    public class ControlLawDto
    {
        public int Index { get; set; }          // モーターの並び順(0=先頭)
        public string Expression { get; set; } = "";
        public bool IsControlled { get; set; }
        public int Mode { get; set; }           // 出力種別 0=電流 1=速度 2=位置 3=拡張位置
        public int ZeroOffset { get; set; }     // 仮想ゼロ点(生値)
    }

    /// <summary>
    /// プリセットを %AppData%\Dynapp\presets\ 以下に1ファイル1プリセットで保存/一覧/読込/削除する。
    /// </summary>
    public static class PresetStore
    {
        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dynapp", "presets");

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "preset" : name.Trim();
        }

        private static string PathFor(string name) => Path.Combine(Dir, SanitizeFileName(name) + ".json");

        /// <summary>保存済みプリセット名の一覧(ファイル名ベース)を返す。</summary>
        public static List<string> List()
        {
            try
            {
                if (!Directory.Exists(Dir)) return new List<string>();
                return Directory.GetFiles(Dir, "*.json")
                                .Select(f => Path.GetFileNameWithoutExtension(f))
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }
            catch { return new List<string>(); }
        }

        public static void Save(ControlPreset preset)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathFor(preset.Name), JsonSerializer.Serialize(preset, _opts));
        }

        public static ControlPreset? Load(string name)
        {
            try
            {
                string path = PathFor(name);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<ControlPreset>(File.ReadAllText(path), _opts);
            }
            catch { return null; }
        }

        public static void Delete(string name)
        {
            try
            {
                string path = PathFor(name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* 削除失敗は無視 */ }
        }
    }
}
