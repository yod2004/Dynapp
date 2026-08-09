using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Dynapp
{
    /// <summary>
    /// 対応表(ルックアップテーブル)変数。アップロードしたファイル(x,y の対応表)に基づき、
    /// 指定した x(式)の現在値から y を線形補間して返す。y = この変数の値。
    /// ファイルは1行につき「x y」または「x,y」の2列。範囲外は端の値で保持(クランプ)。
    /// </summary>
    public class TableVariable : INotifyPropertyChanged
    {
        public TableVariable(string name = "", string xExpression = "")
        {
            _name = name;
            _xExpressionText = xExpression;
            RecompileX();
        }

        private string _name;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; NotifyPropertyChanged(); } }
        }

        // x として使う式(変数名でもよい。例: pos1, a, pos1-pos2)
        private string _xExpressionText;
        public string XExpressionText
        {
            get => _xExpressionText;
            set
            {
                if (_xExpressionText != value)
                {
                    _xExpressionText = value;
                    NotifyPropertyChanged();
                    RecompileX();
                }
            }
        }

        public ControlExpression? XExpression { get; private set; }

        // 対応表の折れ点。Xで昇順ソート済み。
        private double[] _xs = Array.Empty<double>();
        private double[] _ys = Array.Empty<double>();

        private int _pointCount;
        public int PointCount
        {
            get => _pointCount;
            private set { if (_pointCount != value) { _pointCount = value; NotifyPropertyChanged(); } }
        }

        private string _fileName = "(未読込)";
        public string FileName
        {
            get => _fileName;
            private set { if (_fileName != value) { _fileName = value; NotifyPropertyChanged(); } }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            private set { if (_statusText != value) { _statusText = value; NotifyPropertyChanged(); } }
        }

        private bool _isValid;
        public bool IsValid
        {
            get => _isValid;
            private set { if (_isValid != value) { _isValid = value; NotifyPropertyChanged(); } }
        }

        // 補間結果の現在値(y)
        private double _value;
        public double Value
        {
            get => _value;
            set { if (_value != value) { _value = value; NotifyPropertyChanged(); } }
        }

        public bool HasTable => _xs.Length >= 2;

        /// <summary>プリセット保存/復元用に折れ点を直接セットする。</summary>
        public void SetTable(double[] xs, double[] ys, string fileName)
        {
            if (xs != null && ys != null && xs.Length == ys.Length && xs.Length >= 1)
            {
                var pairs = xs.Zip(ys, (x, y) => (x, y)).OrderBy(p => p.x).ToArray();
                _xs = pairs.Select(p => p.x).ToArray();
                _ys = pairs.Select(p => p.y).ToArray();
            }
            else
            {
                _xs = Array.Empty<double>();
                _ys = Array.Empty<double>();
            }
            FileName = string.IsNullOrWhiteSpace(fileName) ? "(表)" : fileName;
            PointCount = _xs.Length;
            UpdateStatus();
        }

        public double[] GetXs() => _xs;
        public double[] GetYs() => _ys;

        /// <summary>ファイルから対応表を読み込む(1行「x y」または「x,y」)。</summary>
        public void LoadFromFile(string path)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var line in File.ReadAllLines(path))
            {
                string s = line.Trim();
                if (s.Length == 0 || s.StartsWith("#") || s.StartsWith("//")) continue;
                var parts = s.Split(new[] { ',', '\t', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                {
                    xs.Add(x);
                    ys.Add(y);
                }
            }
            SetTable(xs.ToArray(), ys.ToArray(), Path.GetFileName(path));
        }

        /// <summary>x に対応する y を線形補間で返す。範囲外は端の値で保持。</summary>
        public double Lookup(double x)
        {
            int n = _xs.Length;
            if (n == 0) return 0;
            if (n == 1) return _ys[0];
            if (x <= _xs[0]) return _ys[0];
            if (x >= _xs[n - 1]) return _ys[n - 1];

            // 2分探索で x が入る区間を見つける
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_xs[mid] <= x) lo = mid; else hi = mid;
            }
            double x0 = _xs[lo], x1 = _xs[hi];
            double y0 = _ys[lo], y1 = _ys[hi];
            double t = (x1 == x0) ? 0 : (x - x0) / (x1 - x0);
            return y0 + (y1 - y0) * t;
        }

        private void RecompileX()
        {
            if (string.IsNullOrWhiteSpace(XExpressionText))
                XExpression = null;
            else
                XExpression = ControlExpression.Parse(XExpressionText);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (!HasTable)
            {
                IsValid = false;
                StatusText = "表が未読込(2点以上必要)";
                return;
            }
            if (XExpression == null || !XExpression.IsValid)
            {
                IsValid = false;
                StatusText = XExpression?.Error ?? "x の式が未入力";
                return;
            }
            IsValid = true;
            StatusText = $"OK ({PointCount}点)";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
