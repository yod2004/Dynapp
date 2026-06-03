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
using System.Linq;

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
        private readonly DynamixelModel _dynamixelModel = new DynamixelModel();
        public MotorViewModel[] Motors { get; }

        public MainViewModel()// コンストラクタ
        {
            RefreshPorts(); // 利用可能なCOMポートを最初に取得しておく
            Motors = new MotorViewModel[]
            {
                new MotorViewModel(1, _dynamixelModel),
                new MotorViewModel(2, _dynamixelModel),
                new MotorViewModel(3, _dynamixelModel)
            };
        }


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

            byte[] targetIds = Motors.Select(m => m.MotorId).ToArray();

            // Task.Run で裏方のスレッド（別作業員）にループ処理を丸投げする
            await Task.Run(async () =>
            {
                while (_IsPolling)
                {
                    var positions = _dynamixelModel.ReadAllPositions(targetIds);
                    if(positions != null)
                    {
                        foreach(var motor in Motors) 
                        {
                            if(positions.ContainsKey(motor.MotorId))
                            {
                                motor.NowValue = positions[motor.MotorId];
                            }
                        }
                    }
                    // 3. 少し休む（50ミリ秒待機 = 1秒間に20回更新）
                    // ※これを入れないと全力で通信してエラーになるので必須です
                    await Task.Delay(50);
                }
            });
        }

        public DelegateCommand AllEnableCommand => new DelegateCommand(AllEnable);
        void AllEnable()
        {
            foreach (var motor in Motors)
            {
               motor.IsEnable = true;
            }
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