using ScottPlot;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Dynapp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // グラフ用のロガー
        private ScottPlot.Plottables.DataLogger[] _loggers = new ScottPlot.Plottables.DataLogger[3];

        // グラフ描画更新用のタイマー
        private DispatcherTimer _renderTimer;
        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel();
            this.DataContext = vm;

            // グラフの初期化
            InitGraph();

            // ViewModelの「電流データ受信イベント」を購読（フック）する
            vm.CurrentDataReceived += OnCurrentDataReceived;

            // グラフを定期的に再描画するタイマーの設定（約30FPS）
            _renderTimer = new DispatcherTimer();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(33);
            _renderTimer.Tick += (s, e) => WpfPlot1.Refresh();
            _renderTimer.Start();
        }
        private void InitGraph()
        {
            var plot = WpfPlot1.Plot;
            plot.Title("Motor Current Monitor");
            plot.YLabel("Current (mA)");

            ScottPlot.Color[] colors = { ScottPlot.Colors.Red, ScottPlot.Colors.Blue, ScottPlot.Colors.Green };
            for (int i = 0; i < 3; i++)
            {
                _loggers[i] = plot.Add.DataLogger();
                _loggers[i].Color = colors[i];
                _loggers[i].LegendText = $"Motor {i + 1}";

                // 【オプション】最新の500データポイントだけ表示してスクロールさせる設定
                _loggers[i].ManageAxisLimits = true;
                _loggers[i].ViewSlide(500);
            }

            plot.ShowLegend(Alignment.UpperRight);
            WpfPlot1.Refresh();
        }
        // ViewModelから裏方スレッドで呼ばれるメソッド
        private void OnCurrentDataReceived(double c1, double c2, double c3)
        {
            // DataLoggerへの追加は別スレッドからでも安全に行えます
            _loggers[0].Add(c1);
            _loggers[1].Add(c2);
            _loggers[2].Add(c3);
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

        public event Action<double, double, double>? CurrentDataReceived;

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
                    // --- 【追加】電流値の取得と通知 ---
                    // ※ DynamixelModelに電流を取得するメソッド (ReadAllCurrents等) を実装する必要があります
                    var currents = _dynamixelModel.ReadAllCurrents(targetIds);
                    if (currents != null)
                    {
                        double c1 = currents.ContainsKey(1) ? currents[1] : 0;
                        double c2 = currents.ContainsKey(2) ? currents[2] : 0;
                        double c3 = currents.ContainsKey(3) ? currents[3] : 0;

                        // View側に「最新の電流値が取れたよ」と通知する
                        CurrentDataReceived?.Invoke(c1, c2, c3);
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