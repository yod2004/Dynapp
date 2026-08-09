using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dynapp
{
    /// <summary>
    /// 制御モードで「監視する変数」1つ分。名前と式を持ち、評価結果(Value)がGUI上で更新される。
    /// 例) 名前="差分角", 式="deg((pos1-pos2)/4096*360)"
    /// </summary>
    public class MonitorItem : INotifyPropertyChanged
    {
        public MonitorItem(string name = "", string expression = "")
        {
            _name = name;
            _expression = expression;
            RecompileExpression();
        }

        private string _name;
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; NotifyPropertyChanged(); } }
        }

        private string _expression;
        public string Expression
        {
            get => _expression;
            set
            {
                if (_expression != value)
                {
                    _expression = value;
                    NotifyPropertyChanged();
                    RecompileExpression();
                }
            }
        }

        // 解析済みの式。評価に使う。
        public ControlExpression? Compiled { get; private set; }

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

        // 監視結果の最新値(制御/ポーリングループから更新される)
        private double _value;
        public double Value
        {
            get => _value;
            set { if (_value != value) { _value = value; NotifyPropertyChanged(); } }
        }

        private void RecompileExpression()
        {
            Compiled = ControlExpression.Parse(Expression);
            IsValid = Compiled.IsValid;
            StatusText = Compiled.IsValid ? "OK" : (Compiled.Error ?? "式エラー");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
