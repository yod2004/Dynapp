using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dynapp
{
    /// <summary>
    /// 制御式で使える「微分変数」1つ分。指定した式の値の時間微分 d/dt を毎周期計算し、
    /// 他の式から名前で参照できる。数値微分はノイズが乗るので、1次ローパス(時定数τ)で平滑化できる。
    /// τ=0なら生の差分微分。例) 名前=phi_dot, 式=phi  →  phi_dot = d(phi)/dt。
    /// </summary>
    public class DerivativeVariable : INotifyPropertyChanged
    {
        public DerivativeVariable(string name = "", string expression = "", double tau = 0.02)
        {
            _name = name;
            _expressionText = expression;
            _tau = tau;
            RecompileExpression();
        }

        private string _name;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; NotifyPropertyChanged(); } }
        }

        // 微分する対象の式
        private string _expressionText;
        public string ExpressionText
        {
            get => _expressionText;
            set
            {
                if (_expressionText != value)
                {
                    _expressionText = value;
                    NotifyPropertyChanged();
                    RecompileExpression();
                }
            }
        }

        public ControlExpression? Expression { get; private set; }

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

        // 平滑化の時定数[秒]。0で生の微分。大きいほどノイズに強いが応答が鈍る。
        private double _tau = 0.02;
        public double Tau
        {
            get => _tau;
            set { double v = value < 0 ? 0 : value; if (_tau != v) { _tau = v; NotifyPropertyChanged(); } }
        }

        // 計算された微分値(制御式から参照される)
        private double _value;
        public double Value
        {
            get => _value;
            set { if (_value != value) { _value = value; NotifyPropertyChanged(); } }
        }

        private double _prev;
        private bool _hasPrev;
        private double _filtered;

        /// <summary>内部状態を初期化する(START時や手動リセット)。</summary>
        public void Reset()
        {
            _hasPrev = false;
            _filtered = 0;
            Value = 0;
        }

        /// <summary>対象式の現在値xと経過時間dtを受け取り、微分値を更新する。</summary>
        public void Update(double x, double dt)
        {
            if (dt <= 0 || double.IsNaN(x) || double.IsInfinity(x)) return;
            if (!_hasPrev)
            {
                // 初回はひとつ前の値が無いので微分は出さない(0のまま)
                _prev = x;
                _hasPrev = true;
                return;
            }
            double raw = (x - _prev) / dt;
            _prev = x;

            double tau = _tau;
            if (tau > 0)
                _filtered += (raw - _filtered) * (dt / (tau + dt)); // 1次ローパス
            else
                _filtered = raw;

            Value = _filtered;
        }

        private void RecompileExpression()
        {
            if (string.IsNullOrWhiteSpace(ExpressionText))
            {
                Expression = null;
                IsValid = false;
                StatusText = "式が未入力";
                return;
            }
            Expression = ControlExpression.Parse(ExpressionText);
            IsValid = Expression.IsValid;
            StatusText = Expression.IsValid ? "OK" : (Expression.Error ?? "式エラー");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
