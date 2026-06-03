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
using System.IO;

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

        private List<string> _ScriptCommands = new List<string>();
        private bool _IsScriptRunning = false;
        private string _ScriptFileName = "選択されていません";
        public string ScriptFileName
        {
            get => _ScriptFileName;
            set
            {
                _ScriptFileName = value;
                NotifyPropertyChanged();
            }
        }

        public DelegateCommand LoadScriptCommand => new DelegateCommand(LoadScript);
        private void LoadScript()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
            if (dialog.ShowDialog() == true)
            {
                _ScriptCommands = File.ReadAllLines(dialog.FileName)
                                     .Where(line => !string.IsNullOrWhiteSpace(line))
                                     .ToList();
                ScriptFileName = $"{System.IO.Path.GetFileName(dialog.FileName)} (全{_ScriptCommands.Count}行)";
            }
        }

        public DelegateCommand RunScriptCommand => new DelegateCommand(RunScript);
        private async void RunScript()
        {
            if (_ScriptCommands.Count == 0)
            {
                ConnectionStatus = "先にテキストファイルを読み込んでください";
                return;
            }
            if (_IsScriptRunning) return;

            _IsScriptRunning = true;
            ConnectionStatus = "スクリプト実行開始...";

            // 裏方スレッドで順番に実行（UIをフリーズさせないため）
            await Task.Run(async () =>
            {
                foreach (var line in _ScriptCommands)
                {
                    if (!_IsScriptRunning) break; // STOPボタンで中断された時用

                    // "3 +500" のように空白で分割
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    if (!int.TryParse(parts[0], out int mask)) continue;
                    string commandStr = parts[1];
                    bool isRelative = commandStr.StartsWith("+") || commandStr.StartsWith("-");
                    if (!int.TryParse(commandStr, out int value)) continue;

                    int[] targetValues = new int[Motors.Length];
                    bool[] isTarget = new bool[Motors.Length];

                    // JSの mask & (1 << index) と全く同じロジック！
                    for (int i = 0; i < Motors.Length; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                        {
                            isTarget[i] = true;
                            var motor = Motors[i];

                            int targetVal = isRelative ? motor.NowValue + value : value;
                            targetVal = Math.Max(motor.SliderMin, Math.Min(motor.SliderMax, targetVal));

                            // WFPの仕様: 画面のUI部品（スライダー等）に紐づく値はメインスレッドで更新する
                            Application.Current.Dispatcher.Invoke(() => {
                                motor.SliderValue = targetVal;
                            });

                            targetValues[i] = targetVal;
                        }
                    }

                    // 位置決め待機ループ (JSの while (this.isRunning) と同じ)
                    int timeoutCounter = 0;
                    while (_IsScriptRunning)
                    {
                        bool allReached = true;
                        for (int i = 0; i < Motors.Length; i++)
                        {
                            if (isTarget[i])
                            {
                                // 誤差10以内かチェック (NowValue は Polling ループが勝手に最新にしてくれている)
                                if (Math.Abs(targetValues[i] - Motors[i].NowValue) > 10)
                                {
                                    allReached = false;
                                }
                            }
                        }

                        if (allReached) break;

                        timeoutCounter++;
                        if (timeoutCounter > 50) break; // 5秒タイムアウト

                        await Task.Delay(100);
                    }
                }

                if (_IsScriptRunning)
                {
                    _IsScriptRunning = false;
                    Application.Current.Dispatcher.Invoke(() => {
                        ConnectionStatus = "スクリプト実行完了";
                    });
                }
            });
        }

        // 3. STOPコマンド (トルクOFFしてループを抜ける)
        public DelegateCommand StopScriptCommand => new DelegateCommand(StopScript);
        private void StopScript()
        {
            _IsScriptRunning = false;
            foreach (var motor in Motors)
            {
                // IsEnable を false にするだけで、裏で勝手に SetTorqueEnable(..., false) が飛ぶ！
                motor.IsEnable = false;
            }
            ConnectionStatus = "緊急停止しました(トルクOFF)";
        }
    }
}