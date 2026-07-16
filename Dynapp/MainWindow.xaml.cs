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
using System.Collections.ObjectModel;
using System.Collections.Specialized; // ★これを追加
using System.Collections.Generic;
using OpenTK.Platform.Windows;

namespace Dynapp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // ★ 配列をやめて「どのモーターが、どのロガーを持っているか」を紐づける辞書にする
        private Dictionary<MotorViewModel, (ScottPlot.Plottables.DataLogger current, ScottPlot.Plottables.DataLogger pos)> _loggerMap = new();
        // 色を順番に割り当てるためのパレットとカウンター
        private ScottPlot.Palettes.Category20 _palette = new ScottPlot.Palettes.Category20();
        private int _colorIndex = 0;
        // グラフ描画更新用のタイマー
        private DispatcherTimer _renderTimer;
        // ★ 追加: ViewModelをクラス全体で共有できるように保持する変数
        private MainViewModel _viewModel;
        public MainWindow()//コンストラクタ
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            // グラフの初期化
            InitGraphBase();

            // 1. 起動時に最初からあるモーターのグラフを作る
            foreach (var motor in _viewModel.Motors)
            {
                AddMotorGraph(motor);
            }

            // 2. 「+1」や「-1」ボタンでモーターが増減した時のイベントを監視する！
            _viewModel.Motors.CollectionChanged += (s, e) =>
            {
                // 増えた時
                if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
                {
                    foreach (MotorViewModel newMotor in e.NewItems)
                        AddMotorGraph(newMotor);
                }
                // 減った時
                else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
                {
                    foreach (MotorViewModel oldMotor in e.OldItems)
                        RemoveMotorGraph(oldMotor);
                }
                // ★ ここを追加！ Clear() でリセットされたらグラフの線も全部消す
                else if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    foreach (var loggers in _loggerMap.Values)
                    {
                        WpfPlot1.Plot.Remove(loggers.current);
                        WpfPlot1.Plot.Remove(loggers.pos);
                    }
                    _loggerMap.Clear();
                    _colorIndex = 0; // 色の順番も最初に戻す
                }

                WpfPlot1.Refresh(); // グラフの凡例などを再描画
            };
            _viewModel.CurrentDataReceived += OnCurrentDataReceived;
            _viewModel.PositionDataReceived += OnPositionDataReceived;

            _renderTimer = new DispatcherTimer();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(33);
            _renderTimer.Tick += (s, e) => WpfPlot1.Refresh();
            _renderTimer.Start();
        }
        // グラフの「軸」などの土台だけを作る
        private void InitGraphBase()
        {
            var plot = WpfPlot1.Plot;
            plot.Title("Motor Monitor");

            // XAML側のダークテーマに合わせた配色
            plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1C1F27");
            plot.DataBackground.Color = ScottPlot.Color.FromHex("#12141A");
            plot.Axes.Color(ScottPlot.Color.FromHex("#8A90A0"));
            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#2A2E3A");
            plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#252934");
            plot.Legend.FontColor = ScottPlot.Color.FromHex("#E8EAF0");
            plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#2A2E3A");
            plot.Axes.Title.Label.ForeColor = ScottPlot.Color.FromHex("#E8EAF0");

            plot.Axes.Left.Label.Text = "Current (mA)";
            plot.Axes.Left.Label.ForeColor = ScottPlot.Color.FromHex("#4F8EF7");
            plot.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#4F8EF7");

            plot.Axes.Right.Label.Text = "Position";
            plot.Axes.Right.Label.ForeColor = ScottPlot.Color.FromHex("#E5484D");
            plot.Axes.Right.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#E5484D");
            WpfPlot1.Plot.ShowLegend(Alignment.UpperLeft);
        }
        // ★ 新しいモーターのグラフ線をScottPlotに追加するメソッド
        private void AddMotorGraph(MotorViewModel motor)
        {
            var plot = WpfPlot1.Plot;

            // Current用ロガー
            var cLogger = plot.Add.DataLogger();
            cLogger.Axes.YAxis = plot.Axes.Left;
            cLogger.Color = _palette.GetColor(_colorIndex * 2);
            cLogger.LegendText = $"Motor {motor.Id} Current";
            cLogger.ManageAxisLimits = true;
            cLogger.ViewSlide(500);
            cLogger.IsVisible = motor.IsShowCurrentGraph;

            // Position用ロガー
            var pLogger = plot.Add.DataLogger();
            pLogger.Axes.YAxis = plot.Axes.Right;
            pLogger.Color = _palette.GetColor(_colorIndex * 2 + 1);
            pLogger.LegendText = $"Motor {motor.Id} Position";
            pLogger.ManageAxisLimits = true;
            pLogger.ViewSlide(500);
            pLogger.IsVisible = motor.IsShowPositionGraph;

            // 辞書に登録
            _loggerMap.Add(motor, (cLogger, pLogger));
            _colorIndex++;

            // チェックボックスの変更監視
            motor.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(MotorViewModel.IsShowCurrentGraph))
                    cLogger.IsVisible = motor.IsShowCurrentGraph;
                else if (e.PropertyName == nameof(MotorViewModel.IsShowPositionGraph))
                    pLogger.IsVisible = motor.IsShowPositionGraph;
            };
        }

        // ★ モーターが減った時にグラフ線をScottPlotから削除するメソッド
        private void RemoveMotorGraph(MotorViewModel motor)
        {
            if (_loggerMap.TryGetValue(motor, out var loggers))
            {
                // Plotから線を消す
                WpfPlot1.Plot.Remove(loggers.current);
                WpfPlot1.Plot.Remove(loggers.pos);

                // 辞書からも削除
                _loggerMap.Remove(motor);
            }
        }
        // データ受信時
        private void OnCurrentDataReceived(double[] currents)
        {
            for (int i = 0; i < currents.Length; i++)
            {
                // インデックスが存在し、かつ辞書に登録されているモーターならデータを追加
                if (i < _viewModel.Motors.Count)
                {
                    var motor = _viewModel.Motors[i];
                    if (_loggerMap.ContainsKey(motor))
                    {
                        _loggerMap[motor].current.Add(currents[i]);
                    }
                }
            }
        }

        private void OnPositionDataReceived(int[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if (i < _viewModel.Motors.Count)
                {
                    var motor = _viewModel.Motors[i];
                    if (_loggerMap.ContainsKey(motor))
                    {
                        _loggerMap[motor].pos.Add(positions[i]);
                    }
                }
            }
        }

        // --- MainWindow.xaml.cs の中に追加 ---

        private void Slider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // イベントの発生元がSliderかどうかを確認
            if (sender is Slider slider)
            {
                // 1回のホイールカチッで動かす量（お好みで変更してください）
                double step = 50;

                // e.Delta は奥に回すとプラス（通常+120）、手前に回すとマイナス（通常-120）になります
                if (e.Delta > 0)
                {
                    // 上限を超えないように足す
                    slider.Value = Math.Min(slider.Maximum, slider.Value + step);
                }
                else if (e.Delta < 0)
                {
                    // 下限を下回らないように引く
                    slider.Value = Math.Max(slider.Minimum, slider.Value - step);
                }

                // これをtrueにすると、「画面全体がスクロールしてしまう」のを防げます
                e.Handled = true;
            }
        }

        // 作動機構テスト用: マウスホイールで回転速度(LinkedSpeed)を微調整する
        private void SpeedSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is Slider slider)
            {
                // 1カチッあたりの速度変化量。細かく調整したいので小さめにしてある
                double step = 10;

                if (e.Delta > 0)
                    slider.Value = Math.Min(slider.Maximum, slider.Value + step);
                else if (e.Delta < 0)
                    slider.Value = Math.Max(slider.Minimum, slider.Value - step);

                e.Handled = true; // 画面全体のスクロールを止める
            }
        }

        // 作動機構テスト用: マウスホイールで連動比(LinkRatio, -1〜1)を微調整する
        private void RatioSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is Slider slider)
            {
                double step = 0.05;

                if (e.Delta > 0)
                    slider.Value = Math.Min(slider.Maximum, slider.Value + step);
                else if (e.Delta < 0)
                    slider.Value = Math.Max(slider.Minimum, slider.Value - step);

                e.Handled = true;
            }
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
        public ObservableCollection<MotorViewModel> Motors { get; } = new ObservableCollection<MotorViewModel>();

        public int MotorCount => Motors.Count;

        public event Action<double[]>? CurrentDataReceived;
        public event Action<int[]>? PositionDataReceived;

        public MainViewModel()// コンストラクタ
        {
            RefreshPorts(); // 利用可能なCOMポートを最初に取得しておく
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
            // Task.Run で裏方のスレッド（別作業員）にループ処理を丸投げする
            await Task.Run(async () =>
            {
                while (_IsPolling)
                {
                    byte[] targetIds = Motors.Select(m => m.MotorId).ToArray();
                    var positions = _dynamixelModel.ReadAllPositions(targetIds);
                    if(positions != null)
                    {
                        int[] pArray = new int[Motors.Count];
                        for(int i = 0; i < Motors.Count; i++)
                        {
                            byte id = Motors[i].MotorId;
                            if (positions.ContainsKey(id))
                            {
                                Motors[i].NowValue = positions[id];
                                pArray[i] = positions[id];
                            }
                        }
                        PositionDataReceived?.Invoke(pArray);
                        // 連動対象2台の位置差分を更新する
                        UpdateLinkedPositionDiff();
                    }

                    // --- 【追加】電流値の取得と通知 ---
                    // ※ DynamixelModelに電流を取得するメソッド (ReadAllCurrents等) を実装する必要があります
                    var currents = _dynamixelModel.ReadAllCurrents(targetIds);
                    if (currents != null)
                    {
                        double[] cArray = new double[Motors.Count];
                        for (int i = 0; i < Motors.Count; i++)
                        {
                            byte id = Motors[i].MotorId;
                            cArray[i] = currents.ContainsKey(id) ? currents[id] : 0;
                        }
                        // 配列ごと通知
                        CurrentDataReceived?.Invoke(cArray);
                    }
                    // 3. 少し休む（50ミリ秒待機 = 1秒間に20回更新）
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

        // ===== 作動機構テスト (速度制御) =====

        // 速度連動テストが実行中かどうか
        private bool _IsVelocityTestRunning = false;
        public bool IsVelocityTestRunning
        {
            get => _IsVelocityTestRunning;
            private set
            {
                _IsVelocityTestRunning = value;
                NotifyPropertyChanged();
            }
        }

        // 1台目のモーターの目標速度(符号付き)。スライダー/マウスホイールで調整する。
        private int _Motor1Speed = 0;
        public int Motor1Speed
        {
            get => _Motor1Speed;
            set
            {
                // Dynamixelの速度指令の一般的な範囲に収める
                int clamped = Math.Max(-1023, Math.Min(1023, value));
                if (_Motor1Speed != clamped)
                {
                    _Motor1Speed = clamped;
                    NotifyPropertyChanged();
                    // テスト実行中なら、速度を変えた瞬間に連動モーターへ反映する
                    if (IsVelocityTestRunning) ApplyAllVelocities();
                }
            }
        }

        // 1台目と2台目の連動比(-1〜1)。
        // 2台目の速度 = 1台目の速度 × この比。 -1で逆回転、0で停止、1で同回転。
        private double _LinkRatio = 1.0;
        public double LinkRatio
        {
            get => _LinkRatio;
            set
            {
                double clamped = Math.Max(-1.0, Math.Min(1.0, value));
                if (_LinkRatio != clamped)
                {
                    _LinkRatio = clamped;
                    NotifyPropertyChanged();
                    // テスト実行中なら、比を変えた瞬間に2台目へ反映する
                    if (IsVelocityTestRunning) ApplyAllVelocities();
                }
            }
        }

        // 連動対象(先頭2台)の現在位置の差分(1台目 - 2台目)。表示用。
        private int _LinkedPositionDiff = 0;
        public int LinkedPositionDiff
        {
            get => _LinkedPositionDiff;
            private set
            {
                if (_LinkedPositionDiff != value)
                {
                    _LinkedPositionDiff = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(LinkedPositionDiffDeg));
                }
            }
        }

        // 上記の差分を度数法(°)に換算した値。差分を2で割ってから4096カウント = 360°で換算する。
        public double LinkedPositionDiffDeg => _LinkedPositionDiff / 2.0 / 4096.0 * 360.0;

        // ゼロ点ボタンで記録する基準差分。表示はこの値からの相対差分になる。
        private int _LinkedPositionDiffOffset = 0;

        // 連動対象2台の生の位置差分(1台目 - 2台目)。ゼロ点計算のため保持する。
        private int _RawLinkedPositionDiff = 0;

        // 連動対象(先頭2台)の現在位置の差分を計算して表示用プロパティに反映する。
        private void UpdateLinkedPositionDiff()
        {
            var linked = Motors.Where(m => m.IsLinked).Take(2).ToList();
            _RawLinkedPositionDiff = linked.Count >= 2 ? linked[0].NowValue - linked[1].NowValue : 0;
            // 記録した基準(オフセット)を引いて、ゼロ点からの相対差分として表示する
            LinkedPositionDiff = _RawLinkedPositionDiff - _LinkedPositionDiffOffset;
        }

        // 現在の差分をゼロ点(0度)として記録するコマンド
        public DelegateCommand ZeroLinkedPositionDiffCommand => new DelegateCommand(ZeroLinkedPositionDiff);
        private void ZeroLinkedPositionDiff()
        {
            _LinkedPositionDiffOffset = _RawLinkedPositionDiff;
            // 表示を即座に更新(ポーリングを待たずに0になる)
            LinkedPositionDiff = 0;
        }

        // 新しく追加したモーターの「連動」変更を監視して、実行中なら即反映する
        private void AttachMotor(MotorViewModel motor)
        {
            motor.PropertyChanged += (s, e) =>
            {
                if (!IsVelocityTestRunning) return;
                if (e.PropertyName == nameof(MotorViewModel.IsLinked))
                    ApplyAllVelocities();
            };
        }

        // 連動対象(先頭から2台)に速度を割り当てる。
        // 1台目 = Motor1Speed、2台目 = Motor1Speed × 連動比。それ以外は0(停止)。
        private void ApplyAllVelocities()
        {
            var linked = Motors.Where(m => m.IsLinked).Take(2).ToList();
            foreach (var motor in Motors)
            {
                int v = 0;
                if (IsVelocityTestRunning)
                {
                    if (linked.Count > 0 && motor == linked[0])
                        v = Motor1Speed;
                    else if (linked.Count > 1 && motor == linked[1])
                        v = (int)Math.Round(Motor1Speed * LinkRatio);
                }
                motor.GoalVelocity = v; // 表示用
                byte id = motor.MotorId;
                Task.Run(() => _dynamixelModel.SetGoalVelocity(id, v));
            }
        }

        public DelegateCommand StartVelocityTestCommand => new DelegateCommand(StartVelocityTest);
        private void StartVelocityTest()
        {
            if (!_dynamixelModel.IsConnected)
            {
                ConnectionStatus = "先にUSB接続してください";
                return;
            }

            var linked = Motors.Where(m => m.IsLinked).Take(2).ToList();
            if (linked.Count == 0)
            {
                ConnectionStatus = "連動させるモーターを選んでください(先頭2台が対象)";
                return;
            }

            ConnectionStatus = "作動機構テスト: 速度モードへ切替中...";

            Task.Run(() =>
            {
                // 動作モードはトルクOFFのときしか書き換えられないので、順番を守る
                foreach (var motor in linked)
                {
                    byte id = motor.MotorId;
                    _dynamixelModel.SetTorqueEnable(id, false);
                    _dynamixelModel.SetOperatingMode(id, DynamixelModel.OP_MODE_VELOCITY);
                    _dynamixelModel.SetTorqueEnable(id, true);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 画面のENABLEチェックも連動して入れておく
                    foreach (var motor in linked) motor.IsEnable = true;
                    IsVelocityTestRunning = true;
                    ApplyAllVelocities();
                    ConnectionStatus = $"作動機構テスト実行中 ({linked.Count}台連動)";
                });
            });
        }

        public DelegateCommand StopVelocityTestCommand => new DelegateCommand(StopVelocityTest);
        private void StopVelocityTest()
        {
            IsVelocityTestRunning = false;
            // 連動対象を止める(トルクは入れたまま速度0で停止させる)
            foreach (var motor in Motors)
            {
                motor.GoalVelocity = 0;
                byte id = motor.MotorId;
                Task.Run(() => _dynamixelModel.SetGoalVelocity(id, 0));
            }
            ConnectionStatus = "作動機構テスト停止 (速度0)";
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

                    int[] targetValues = new int[Motors.Count];
                    bool[] isTarget = new bool[Motors.Count];

                    // JSの mask & (1 << index) と全く同じロジック！
                    for (int i = 0; i < Motors.Count; i++)
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
                        for (int i = 0; i < Motors.Count; i++)
                        {
                            if (isTarget[i])
                            {
                                // 誤差10以内かチェック (NowValue は Polling ループが勝手に最新にしてくれている)
                                if (Math.Abs(targetValues[i] - Motors[i].NowValue) > 20)
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

        public DelegateCommand MotorCountMinusCommand => new DelegateCommand(MotorCountMinus);
        private void MotorCountMinus()
        {
            if(Motors.Count > 1)
            {
                Motors.RemoveAt(Motors.Count - 1);
                NotifyPropertyChanged(nameof(MotorCount));
            }
        }
        public DelegateCommand MotorCountPlusCommand => new DelegateCommand(MotorCountPlus);
        private void MotorCountPlus()
        {
            byte newId = (byte)(Motors.Count + 1);
            var newMotor = new MotorViewModel(newId, _dynamixelModel);
            AttachMotor(newMotor);
            Motors.Add(newMotor);
            NotifyPropertyChanged(nameof(MotorCount));
        }
        public DelegateCommand ScanCommand => new DelegateCommand(Scan);

        private async void Scan()
        {
            // USBが繋がっていない場合は弾く
            if (ConnectionStatus == "未接続" || ConnectionStatus.Contains("失敗") || ConnectionStatus.Contains("選ばれて"))
            {
                ConnectionStatus = "先にUSB接続してください";
                return;
            }

            ConnectionStatus = "モーターをスキャン中...";

            // スキャン処理は時間がかかる（数秒）ので、UIをフリーズさせないよう裏方スレッドで実行
            var detectedList = await Task.Run(() => _dynamixelModel.ScanMotors());

            // 既存のリストを一旦クリア
            Motors.Clear();

            // 発見したモーターを自動追加
            foreach (var info in detectedList)
            {
                var newMotor = new MotorViewModel(info.Id, _dynamixelModel);
                newMotor.ModelNumber = (ushort)info.ModelNumber; // ★ XMかXCかも自動判別してセット！
                AttachMotor(newMotor);
                Motors.Add(newMotor);
            }

            NotifyPropertyChanged(nameof(MotorCount));
            ConnectionStatus = $"スキャン完了: {Motors.Count}個のモーターを発見";
        }
    }
}