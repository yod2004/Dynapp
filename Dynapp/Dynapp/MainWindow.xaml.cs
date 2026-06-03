using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO.Ports;

namespace Dynapp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel();
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainViewModel()// コンストラクタ
        {
            RefreshPorts(); // 利用可能なCOMポートを最初に取得しておく
        }

        private readonly DynamixelModel _dynamixelModel = new DynamixelModel();

        private string _ConnectionStatus = "未接続";
        public string ConnectionStatus
        {
            get => _ConnectionStatus;
            set
            {
                _ConnectionStatus = value;
                NotifyPropertyChanged();
            }
        }
        // ループを回し続けるかどうかのフラグ
        private bool _IsPolling = false;
        /// <summary>
        /// 裏側でモーターの情報を定期的に取得し続ける非同期メソッド
        /// </summary>
        private async void StartPolling()
        {
            _IsPolling = true;

            // Task.Run で裏方のスレッド（別作業員）にループ処理を丸投げする
            await Task.Run(async () =>
            {
                while (_IsPolling)
                {
                    // 1. Modelを使って現在位置を取得
                    int currentPos = _dynamixelModel.GetPresentPosition(1);

                    // 2. ViewModelのプロパティを更新（すると自動的に画面の数字が変わる！）
                    Motor1NowValue = currentPos;

                    // 3. 少し休む（50ミリ秒待機 = 1秒間に20回更新）
                    // ※これを入れないと全力で通信してエラーになるので必須です
                    await Task.Delay(50);
                }
            });
        }

        public DelegateCommand UsbConnectCommand => new DelegateCommand(UsbConnect);
        void UsbConnect()
        {
            // 決め打ちだった "COM3" を消して、選択されたポートを使う！
            if (string.IsNullOrEmpty(SelectedPort))
            {
                ConnectionStatus = "ポートが選ばれていません";
                return;
            }

            string portName = SelectedPort; // 画面で選んだポート名が入る
            int baudRate = 57600;

            ConnectionStatus = "接続中...";
            bool isSuccess = _dynamixelModel.Connect(portName, baudRate);

            if (isSuccess)
            {
                ConnectionStatus = $"{portName} に接続されました";
                StartPolling(); // (前回追加したループ処理)
            }
            else
            {
                ConnectionStatus = "接続失敗";
            }
        }

        private bool _IsMotor1Led;
        public bool IsMotor1Led
        {
            get { return _IsMotor1Led; }
            set
            {
                if (_IsMotor1Led != value)
                {
                    _IsMotor1Led = value;
                    NotifyPropertyChanged(nameof(IsMotor1Led));
                    _dynamixelModel.SetLed(1, value);
                }
            }
        }
        private bool _IsMotor1Enable;
        public bool IsMotor1Enable
        {
            get { return _IsMotor1Enable; }
            set
            {
                if (_IsMotor1Enable != value)
                {
                    _IsMotor1Enable = value;
                    NotifyPropertyChanged(nameof(IsMotor1Enable));
                    _dynamixelModel.SetTorqueEnable(1, value);
                }
            }
        }
        private int _Motor1ModeIndex = 3;
        public int Motor1ModeIndex
        {
            get { return _Motor1ModeIndex; }
            set
            {
                if (_Motor1ModeIndex != value)
                {
                    _Motor1ModeIndex = value;
                    NotifyPropertyChanged(nameof(Motor1ModeIndex));
                }
            }
        }

        private int _Motor1NowValue = 0;
        public int Motor1NowValue
        {
            get { return _Motor1NowValue; }
            set
            {
                if (_Motor1NowValue != value)
                {
                    _Motor1NowValue = value;
                    NotifyPropertyChanged(nameof(Motor1NowValue));
                }
            }
        }

        private int _Motor1TargetValue = 0;
        public int Motor1TargetValue
        {
            get { return _Motor1TargetValue; }
            set
            {
                if (_Motor1TargetValue != value)
                {
                    _Motor1TargetValue = value;
                    NotifyPropertyChanged(nameof(Motor1TargetValue));
                    _dynamixelModel.SetGoalPosition(1, value);
                }
            }
        }

        private int _Motor1SliderMin = -4095;
        public int Motor1SliderMin
        {
            get { return _Motor1SliderMin; }
            set
            {
                if (_Motor1SliderMin != value)
                {
                    _Motor1SliderMin = value;
                    NotifyPropertyChanged(nameof(Motor1SliderMin));
                }
            }
        }

        private int _Motor1SliderMax = 4095;
        public int Motor1SliderMax
        {
            get { return _Motor1SliderMax; }
            set
            {
                if (_Motor1SliderMax != value)
                {
                    _Motor1SliderMax = value;
                    NotifyPropertyChanged(nameof(Motor1SliderMax));
                }
            }
        }

        private int _Motor1SliderValue = 0;
        public int Motor1SliderValue
        {
            get { return _Motor1SliderValue; }
            set
            {
                Motor1TargetValue = value;
                if (_Motor1SliderValue != value)
                {
                    _Motor1SliderValue = value;
                    NotifyPropertyChanged(nameof(Motor1SliderValue));
                }
            }
        }

        private string[] _AvailablePorts = Array.Empty<string>();
        public string[] AvailablePorts
        {
            get => _AvailablePorts;
            set
            {
                _AvailablePorts = value;
                NotifyPropertyChanged();
            }
        }

        private string? _SelectedPort;
        public string? SelectedPort
        {
            get => _SelectedPort;
            set
            {
                _SelectedPort = value;
                NotifyPropertyChanged();
            }
        }

        public DelegateCommand RefreshPortsCommand => new DelegateCommand(RefreshPorts);
        private void RefreshPorts()
        {
            AvailablePorts = SerialPort.GetPortNames();
            if(AvailablePorts.Length > 0)
            {
                SelectedPort = AvailablePorts[0];
            }
            else
            {
                SelectedPort = null;
            }
        }
    }
}