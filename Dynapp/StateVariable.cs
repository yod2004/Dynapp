using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dynapp
{
    /// <summary>
    /// 制御式で使える「積分変数(状態変数)」1つ分。
    /// 毎制御周期に「被積分の式(Integrand)の値 × dt」を積み上げて Value を更新する。
    /// 例) 名前=ephi_i, 被積分=phi_t-(pos1-pos2)/2  →  Value = ∫誤差dt となり、I制御に使える。
    /// アンチワインドアップとして Value を ±Limit にクランプする。START時にリセットされる。
    /// </summary>
    public class StateVariable : INotifyPropertyChanged
    {
        public StateVariable(string name = "", string integrand = "", double limit = 100000)
        {
            _name = name;
            _integrandText = integrand;
            _limit = limit;
            RecompileIntegrand();
        }

        private string _name;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; NotifyPropertyChanged(); } }
        }

        // 被積分の式(この値を時間積分する)
        private string _integrandText;
        public string IntegrandText
        {
            get => _integrandText;
            set
            {
                if (_integrandText != value)
                {
                    _integrandText = value;
                    NotifyPropertyChanged();
                    RecompileIntegrand();
                }
            }
        }

        public ControlExpression? Integrand { get; private set; }

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

        // アンチワインドアップ用の積分値の絶対値上限
        private double _limit = 100000;
        public double Limit
        {
            get => _limit;
            set { if (_limit != value) { _limit = value < 0 ? 0 : value; NotifyPropertyChanged(); } }
        }

        // 積分された現在値(制御式から参照される)
        private double _value;
        public double Value
        {
            get => _value;
            set { if (_value != value) { _value = value; NotifyPropertyChanged(); } }
        }

        /// <summary>積分値を0に戻す(START時や手動リセット)。</summary>
        public void Reset() => Value = 0;

        /// <summary>被積分の値と経過時間dtを受け取り、積分してクランプする。</summary>
        public void Integrate(double integrandValue, double dt)
        {
            if (double.IsNaN(integrandValue) || double.IsInfinity(integrandValue)) return;
            double v = _value + integrandValue * dt;
            if (v > _limit) v = _limit;
            else if (v < -_limit) v = -_limit;
            Value = v;
        }

        private void RecompileIntegrand()
        {
            if (string.IsNullOrWhiteSpace(IntegrandText))
            {
                Integrand = null;
                IsValid = false;
                StatusText = "式が未入力";
                return;
            }
            Integrand = ControlExpression.Parse(IntegrandText);
            IsValid = Integrand.IsValid;
            StatusText = Integrand.IsValid ? "OK" : (Integrand.Error ?? "式エラー");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
