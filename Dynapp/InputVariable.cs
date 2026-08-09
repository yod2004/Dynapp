using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dynapp
{
    /// <summary>
    /// 制御式で使える「入力変数」1つ分。名前と値を持ち、値はスライダー(マウスホイール)で
    /// 実行中にリアルタイム操作できる。ゲイン k, d もこの入力変数として扱う。
    /// マウスホイール1ステップの変化量は (Max - Min) / Steps。
    /// </summary>
    public class InputVariable : INotifyPropertyChanged
    {
        public InputVariable(string name = "", double value = 0,
                             double min = -100, double max = 100, int steps = 100)
        {
            _name = name;
            _value = value;
            _min = min;
            _max = max;
            _steps = steps;
        }

        private string _name;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; NotifyPropertyChanged(); } }
        }

        // 制御式で参照される現在値。スライダー/ホイール/直接入力で変わる。
        private double _value;
        public double Value
        {
            get => _value;
            set { if (_value != value) { _value = value; NotifyPropertyChanged(); } }
        }

        private double _min;
        public double Min
        {
            get => _min;
            set { if (_min != value) { _min = value; NotifyPropertyChanged(); NotifyPropertyChanged(nameof(StepSize)); } }
        }

        private double _max;
        public double Max
        {
            get => _max;
            set { if (_max != value) { _max = value; NotifyPropertyChanged(); NotifyPropertyChanged(nameof(StepSize)); } }
        }

        // マウスホイールでMin〜Maxを何等分するか
        private int _steps = 100;
        public int Steps
        {
            get => _steps;
            set
            {
                int clamped = value < 1 ? 1 : value;
                if (_steps != clamped) { _steps = clamped; NotifyPropertyChanged(); NotifyPropertyChanged(nameof(StepSize)); }
            }
        }

        // マウスホイール1ステップあたりの変化量
        public double StepSize
        {
            get
            {
                double span = Max - Min;
                int s = Steps < 1 ? 1 : Steps;
                double step = span / s;
                return step == 0 ? 0 : System.Math.Abs(step);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
