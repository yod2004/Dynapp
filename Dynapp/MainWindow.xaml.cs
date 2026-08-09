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

        // 制御モードの入力変数スライダー: マウスホイールで (Max-Min)/Steps ずつ動かす
        private void InputSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is Slider slider && slider.DataContext is InputVariable iv)
            {
                double step = iv.StepSize;
                if (step <= 0) step = (slider.Maximum - slider.Minimum) / 100.0;
                if (step <= 0) { e.Handled = true; return; }

                double v = slider.Value + (e.Delta > 0 ? step : -step);
                v = Math.Max(slider.Minimum, Math.Min(slider.Maximum, v));
                slider.Value = v;
                e.Handled = true; // 画面全体のスクロールを止める
            }
        }

        // --- 変数リストのドラッグ並べ替え ---
        private const string DragFormat = "DynappRowItem";
        private Point _dragStartPoint;
        private object? _dragItem;
        private ItemsControl? _dragSourceItemsControl;

        // グリップを押した瞬間: ドラッグ対象の行(アイテム)と、その所属リスト(ItemsControl)を覚える
        private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                _dragStartPoint = e.GetPosition(null);
                _dragItem = fe.DataContext;
                _dragSourceItemsControl = FindAncestor<ItemsControl>(fe);
            }
        }

        // 一定距離動いたらドラッグ開始
        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null || _dragSourceItemsControl == null) return;
            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var data = new DataObject(DragFormat, _dragItem);
            try { DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move); }
            finally { _dragItem = null; }
        }

        private void Row_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        // 行にドロップ: 同じリスト内でドラッグ元をドロップ先の位置へ移動する
        private void Row_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DragFormat)) return;
            if (sender is not FrameworkElement fe) return;

            object dragged = e.Data.GetData(DragFormat);
            object target = fe.DataContext;
            var ic = FindAncestor<ItemsControl>(fe);
            if (ic == null || ic != _dragSourceItemsControl) return; // 別のリストへは移動しない

            if (ic.ItemsSource is System.Collections.IList list)
            {
                int from = list.IndexOf(dragged);
                int to = list.IndexOf(target);
                if (from >= 0 && to >= 0 && from != to)
                {
                    // ObservableCollection<T>.Move(from,to) をリフレクションで呼ぶ(型に依らず動かせる)
                    var move = ic.ItemsSource.GetType().GetMethod("Move", new[] { typeof(int), typeof(int) });
                    if (move != null) move.Invoke(ic.ItemsSource, new object[] { from, to });
                    else { list.RemoveAt(from); list.Insert(to, dragged); }
                    _viewModel.RebuildTokens(); // 並び順が変わったのでトークン一覧も更新
                }
            }
            e.Handled = true;
        }

        private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            DependencyObject? d = start;
            while (d != null)
            {
                if (d is T t) return t;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        // --- 制御モード: 制御式の入力欄への変数トークン挿入 ---

        // 最後にフォーカスされた式入力用TextBox。パレットのチップはここへ挿入する。
        private TextBox? _activeExpressionBox;

        // 制御式/監視式のTextBoxがフォーカスされたら覚えておく
        private void ExprBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb) _activeExpressionBox = tb;
        }

        // パレットのチップ(変数・演算子)をクリックしたら、覚えているTextBoxのカーソル位置に挿入する
        private void TokenChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string token = btn.Content?.ToString() ?? "";
            if (string.IsNullOrEmpty(token)) return;

            var box = _activeExpressionBox;
            if (box == null) return; // まだどの式欄も選ばれていない

            int caret = box.SelectionStart;
            // 選択範囲があれば置き換え、なければカーソル位置に挿入する
            string text = box.Text ?? "";
            text = text.Remove(caret, box.SelectionLength);
            box.Text = text.Insert(caret, token);
            box.SelectionStart = caret + token.Length;
            box.SelectionLength = 0;
            box.Focus();
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
            SeedDefaultControlConfig(); // 既定の入力変数・式変数・積分変数をセットアップ
            // モーター/入力変数の増減に合わせて、制御式に使えるトークン一覧を作り直す
            Motors.CollectionChanged += (s, e) => RebuildTokens();
            RebuildTokens();
            RefreshPresets(); // 保存済みプリセット一覧を読み込む
        }

        // 各モーターに投入する既定の制御式(index 0 = M1, 1 = M2)。
        // c1,c2 は式変数側で組んだ各モーターへの指令(摩擦補償ffa/ffbはai/bi内に含めてある)。
        private static readonly string[] _defaultControlLaws = new[]
        {
            "c1",
            "c2",
        };

        // 新規モーター(SCAN/追加時)に投入する制御式。既定は上のハードコード。
        // プリセットを読み込むとその内容に差し替わり、以後SCANしたモーターにも反映される。
        private string[] _activeControlLaws = _defaultControlLaws;
        // 同上の出力モード(0=電流…)。既定は空(=全て電流)。プリセット読込で差し替わる。
        private int[] _activeControlModes = Array.Empty<int>();

        // 起動時のデフォルト設定(入力変数・式変数・積分変数・微分変数)をセットアップする。
        // 制御式はモーターが作られた時に AttachMotor で自動投入する。
        private void SeedDefaultControlConfig()
        {
            // --- 入力変数(スライダーで操作するゲイン・目標値) ---
            void AddIn(string name, double val, double min, double max, int steps)
            {
                var iv = new InputVariable(name, val, min, max, steps);
                iv.PropertyChanged += Input_PropertyChanged;
                Inputs.Add(iv);
            }
            AddIn("pa",  0.281,   0,   1, 100);
            AddIn("ia",  0,       0,   1, 100);
            AddIn("da",  0.629,   0,   1, 100);
            AddIn("pb",  0.46,    0,   2, 100);
            AddIn("ib",  0.539,   0,   1, 100);
            AddIn("db",  0,       0,   1, 100);
            AddIn("ffa", 0,       0, 100, 100);
            AddIn("ar",  0,    -500, 500, 100); // 目標値は既定0
            AddIn("br",  0,    -100, 100, 100); // 目標値は既定0
            AddIn("ffb", 42.361,  0, 100, 100);

            // --- 積分変数(I制御用。START時にリセットされる) ---
            void AddState(string name, string integrand, double limit)
            {
                var sv = new StateVariable(name, integrand, limit);
                sv.PropertyChanged += State_PropertyChanged;
                States.Add(sv);
            }
            AddState("inta", "ae", 100000);
            AddState("intb", "be", 100000);

            // --- 微分変数(D制御用。START時にリセットされる) ---
            void AddDer(string name, string expr, double tau)
            {
                var dv = new DerivativeVariable(name, expr, tau);
                dv.PropertyChanged += Derivative_PropertyChanged;
                Derivatives.Add(dv);
            }
            AddDer("delar", "ar", 0.02);
            AddDer("delb",  "b",  0.02);

            // --- 式変数(エイリアス。順に評価されるので依存順に並べる) ---
            void AddDef(string name, string expr)
            {
                var d = new MonitorItem(name, expr);
                d.PropertyChanged += Define_PropertyChanged;
                Defines.Add(d);
            }
            AddDef("a",    "(pos1-pos2)/2");
            AddDef("b",    "-(vel1+vel2)/2");
            AddDef("ae",   "ar-a");
            AddDef("be",   "br-b");
            AddDef("dela", "-(vel1-vel2)/2");
            AddDef("ai",   "pa*ae+ia*inta+da*dela+ffa*sign(pa*ae+ia*inta+da*dela)");
            AddDef("bi",   "pb*be+ib*intb+db*delb+ffb*sign(pb*be+ib*intb+db*delb)");
            AddDef("c1",   "(ai-bi)/2");
            AddDef("c2",   "(-ai-bi)/2");
        }

        // ===== プリセット (保存/読込/削除) =====

        // 保存済みプリセット名の一覧
        public ObservableCollection<string> PresetNames { get; } = new ObservableCollection<string>();

        private string? _SelectedPresetName;
        public string? SelectedPresetName
        {
            get => _SelectedPresetName;
            set
            {
                if (_SelectedPresetName != value)
                {
                    _SelectedPresetName = value;
                    NotifyPropertyChanged();
                    // 選択したら保存名にも入れておく(上書き保存しやすいように)
                    if (!string.IsNullOrWhiteSpace(value)) NewPresetName = value;
                }
            }
        }

        // 「名前を付けて保存」用の入力
        private string _NewPresetName = "";
        public string NewPresetName
        {
            get => _NewPresetName;
            set { if (_NewPresetName != value) { _NewPresetName = value; NotifyPropertyChanged(); } }
        }

        public DelegateCommand RefreshPresetsCommand => new DelegateCommand(RefreshPresets);
        private void RefreshPresets()
        {
            var names = PresetStore.List();
            PresetNames.Clear();
            foreach (var n in names) PresetNames.Add(n);
        }

        public DelegateCommand SavePresetCommand => new DelegateCommand(SavePreset);
        private void SavePreset()
        {
            string name = string.IsNullOrWhiteSpace(NewPresetName) ? (SelectedPresetName ?? "") : NewPresetName;
            if (string.IsNullOrWhiteSpace(name))
            {
                ControlStatus = "プリセット名を入力してください";
                return;
            }
            try
            {
                PresetStore.Save(CapturePreset(name.Trim()));
                RefreshPresets();
                SelectedPresetName = name.Trim();
                ControlStatus = $"プリセット「{name.Trim()}」を保存しました";
            }
            catch (Exception ex)
            {
                ControlStatus = "保存に失敗: " + ex.Message;
            }
        }

        public DelegateCommand LoadPresetCommand => new DelegateCommand(LoadPreset);
        private void LoadPreset()
        {
            if (string.IsNullOrWhiteSpace(SelectedPresetName))
            {
                ControlStatus = "読み込むプリセットを選んでください";
                return;
            }
            var preset = PresetStore.Load(SelectedPresetName);
            if (preset == null)
            {
                ControlStatus = "プリセットの読込に失敗しました";
                return;
            }
            ApplyPreset(preset);
            ControlStatus = $"プリセット「{SelectedPresetName}」を読み込みました";
        }

        public DelegateCommand DeletePresetCommand => new DelegateCommand(DeletePreset);
        private void DeletePreset()
        {
            if (string.IsNullOrWhiteSpace(SelectedPresetName)) return;
            PresetStore.Delete(SelectedPresetName);
            ControlStatus = $"プリセット「{SelectedPresetName}」を削除しました";
            SelectedPresetName = null;
            RefreshPresets();
        }

        // 現在の制御設定をプリセット(DTO)へ写し取る
        private ControlPreset CapturePreset(string name)
        {
            var p = new ControlPreset
            {
                Name = name,
                ControlCurrentLimit = ControlCurrentLimit,
                ControlLoopDelayMs = ControlLoopDelayMs,
            };
            foreach (var iv in Inputs)
                p.Inputs.Add(new InputDto { Name = iv.Name, Value = iv.Value, Min = iv.Min, Max = iv.Max, Steps = iv.Steps });
            foreach (var sv in States)
                p.States.Add(new StateDto { Name = sv.Name, Integrand = sv.IntegrandText, Limit = sv.Limit });
            foreach (var dv in Derivatives)
                p.Derivatives.Add(new DerivativeDto { Name = dv.Name, Expression = dv.ExpressionText, Tau = dv.Tau });
            foreach (var tv in Tables)
                p.Tables.Add(new TableDto { Name = tv.Name, XExpression = tv.XExpressionText, FileName = tv.FileName, Xs = tv.GetXs(), Ys = tv.GetYs() });
            foreach (var d in Defines)
                p.Defines.Add(new ExprDto { Name = d.Name, Expression = d.Expression });
            foreach (var m in Monitors)
                p.Monitors.Add(new ExprDto { Name = m.Name, Expression = m.Expression });
            for (int i = 0; i < Motors.Count; i++)
            {
                var mt = Motors[i];
                if (mt.IsControlled || !string.IsNullOrWhiteSpace(mt.ControlExpressionText) || mt.PositionZeroOffset != 0)
                    p.ControlLaws.Add(new ControlLawDto { Index = i, Expression = mt.ControlExpressionText, IsControlled = mt.IsControlled, Mode = mt.ControlModeIndex, ZeroOffset = mt.PositionZeroOffset });
            }
            return p;
        }

        // プリセット(DTO)を現在の制御設定へ反映する
        private void ApplyPreset(ControlPreset p)
        {
            // 既存コレクションを購読解除してクリア
            foreach (var iv in Inputs) iv.PropertyChanged -= Input_PropertyChanged;
            Inputs.Clear();
            foreach (var sv in States) sv.PropertyChanged -= State_PropertyChanged;
            States.Clear();
            foreach (var dv in Derivatives) dv.PropertyChanged -= Derivative_PropertyChanged;
            Derivatives.Clear();
            foreach (var tv in Tables) tv.PropertyChanged -= Table_PropertyChanged;
            Tables.Clear();
            foreach (var d in Defines) d.PropertyChanged -= Define_PropertyChanged;
            Defines.Clear();
            Monitors.Clear();

            // 追加(購読も張り直す)
            foreach (var dto in p.Inputs)
            {
                var iv = new InputVariable(dto.Name, dto.Value, dto.Min, dto.Max, dto.Steps);
                iv.PropertyChanged += Input_PropertyChanged;
                Inputs.Add(iv);
            }
            foreach (var dto in p.States)
            {
                var sv = new StateVariable(dto.Name, dto.Integrand, dto.Limit);
                sv.PropertyChanged += State_PropertyChanged;
                States.Add(sv);
            }
            foreach (var dto in p.Derivatives)
            {
                var dv = new DerivativeVariable(dto.Name, dto.Expression, dto.Tau);
                dv.PropertyChanged += Derivative_PropertyChanged;
                Derivatives.Add(dv);
            }
            foreach (var dto in p.Tables)
            {
                var tv = new TableVariable(dto.Name, dto.XExpression);
                tv.SetTable(dto.Xs, dto.Ys, dto.FileName);
                tv.PropertyChanged += Table_PropertyChanged;
                Tables.Add(tv);
            }
            foreach (var dto in p.Defines)
            {
                var d = new MonitorItem(dto.Name, dto.Expression);
                d.PropertyChanged += Define_PropertyChanged;
                Defines.Add(d);
            }
            foreach (var dto in p.Monitors)
                Monitors.Add(new MonitorItem(dto.Name, dto.Expression));

            ControlCurrentLimit = (int)Math.Round(p.ControlCurrentLimit);
            ControlLoopDelayMs = p.ControlLoopDelayMs;

            // 制御式: 今後SCAN/追加されるモーター用に記憶し、既存モーターにも即適用する
            if (p.ControlLaws.Count > 0)
            {
                int maxIdx = p.ControlLaws.Max(l => l.Index);
                var laws = new string[maxIdx + 1];
                var modes = new int[maxIdx + 1];
                for (int i = 0; i < laws.Length; i++) laws[i] = "";
                foreach (var l in p.ControlLaws)
                    if (l.Index >= 0 && l.Index < laws.Length) { laws[l.Index] = l.Expression; modes[l.Index] = l.Mode; }
                _activeControlLaws = laws;
                _activeControlModes = modes;

                foreach (var l in p.ControlLaws)
                {
                    if (l.Index >= 0 && l.Index < Motors.Count)
                    {
                        Motors[l.Index].ControlExpressionText = l.Expression;
                        Motors[l.Index].IsControlled = l.IsControlled;
                        Motors[l.Index].ControlModeIndex = l.Mode;
                        Motors[l.Index].PositionZeroOffset = l.ZeroOffset;
                    }
                }
            }

            _lastDerived.Clear();  // 削除された変数への古い参照をクリア
            RebuildTokens();
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


        // Windowsのタイマ分解能を上げる/戻す。既定では約15.6msに丸められ、Task.Delay(数ms)が効かない。
        // 1msにすることで「制御周期 ms」を小さくした効果が実際に出るようにする。
        [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        /// <summary>
        /// 裏側でモーターの情報を定期的に取得し続ける非同期メソッド
        /// </summary>
        private async void StartPolling()
        {
            _IsPolling = true;
            // システムタイマ分解能を1msに上げる(Task.Delayの丸めを防ぎ、周期を実際に詰められるようにする)
            try { TimeBeginPeriod(1); } catch { /* winmmが無い環境でも致命的でない */ }
            // Task.Run で裏方のスレッド（別作業員）にループ処理を丸投げする
            await Task.Run(async () =>
            {
                long cycle = 0;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                double lastT = stopwatch.Elapsed.TotalSeconds;
                double hzWindowStart = lastT;   // 実測Hz集計の窓の開始時刻
                long hzWindowCount = 0;          // 窓内のループ実行回数
                while (_IsPolling)
                {
                    // 制御周期はUIから変更できる。最低2msでガード。
                    int delay = Math.Max(2, ControlLoopDelayMs);

                    byte[] targetIds = Motors.Select(m => m.MotorId).ToArray();

                    // --- 位置・速度・電流を「1回の通信」でまとめて取得 ---
                    // 連続アドレス(126〜135)を1回のGroupSyncReadで読むことで、読み取りを1トランザクションに集約。
                    var states = _dynamixelModel.ReadAllStates(targetIds);
                    if (states != null)
                    {
                        int[] pArray = new int[Motors.Count];
                        double[] cArray = new double[Motors.Count];
                        for (int i = 0; i < Motors.Count; i++)
                        {
                            byte id = Motors[i].MotorId;
                            if (states.TryGetValue(id, out var st))
                            {
                                Motors[i].NowValue = st.Position;        // 表示 & 制御式 pos<ID>
                                Motors[i].PresentVelocity = st.Velocity; // 制御式 vel<ID>
                                Motors[i].PresentCurrent = st.Current;   // 制御式 cur<ID>
                                pArray[i] = st.Position;
                                cArray[i] = st.Current;
                            }
                        }
                        PositionDataReceived?.Invoke(pArray);
                        CurrentDataReceived?.Invoke(cArray);
                        // 連動対象2台の位置差分を更新する
                        UpdateLinkedPositionDiff();
                    }

                    // --- 制御モード: トルクを計算して一括送信 & 監視変数を更新 ---
                    // dtは実経過時間(秒)。長すぎる値(初回やストール後)は積分の飛びを防ぐため上限0.2sで抑える。
                    double now = stopwatch.Elapsed.TotalSeconds;
                    double dt = Math.Min(0.2, now - lastT);
                    lastT = now;
                    RunControlStep(dt);

                    // 実測Hz: 0.5秒ごとに「窓内の実行回数 ÷ 経過時間」で更新する
                    hzWindowCount++;
                    double winElapsed = now - hzWindowStart;
                    if (winElapsed >= 0.5)
                    {
                        MeasuredHz = hzWindowCount / winElapsed;
                        hzWindowStart = now;
                        hzWindowCount = 0;
                    }

                    cycle++;
                    await Task.Delay(delay);
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
            // 先頭2台には既定の制御式を自動投入し、制御対象にしておく(空欄のときだけ)。
            // AttachMotor は Motors.Add の直前に呼ばれるので、Motors.Count が新しいモーターの並び順になる。
            int index = Motors.Count;
            if (index < _activeControlLaws.Length && string.IsNullOrWhiteSpace(motor.ControlExpressionText)
                && !string.IsNullOrWhiteSpace(_activeControlLaws[index]))
            {
                motor.ControlExpressionText = _activeControlLaws[index];
                motor.IsControlled = true;
                if (index < _activeControlModes.Length) motor.ControlModeIndex = _activeControlModes[index];
            }

            motor.PropertyChanged += (s, e) =>
            {
                // モーターIDが変わったら制御式のトークン(pos1, vel1...)を作り直す
                if (e.PropertyName == nameof(MotorViewModel.Id))
                    RebuildTokens();

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

        // ===== 制御モード (トルク制御) =====
        // 各モーターへの入力を「トルク(目標電流)」で与える。トルクは他モーターの
        // 速度・位置情報と、自由に追加できる入力変数(ゲイン k, d など)を使って
        // GUIで組み立てた制御式から計算する。

        // 制御式で使える入力変数のリスト(名前+値)。スライダー(ホイール)で実行中に操作できる。
        // 既定でゲイン k, d を入れておく(既存の式との互換のため)。
        public ObservableCollection<InputVariable> Inputs { get; } = new ObservableCollection<InputVariable>();

        public DelegateCommand AddInputCommand => new DelegateCommand(AddInput);
        private void AddInput()
        {
            var iv = new InputVariable($"in{Inputs.Count + 1}", 0, -100, 100, 100);
            iv.PropertyChanged += Input_PropertyChanged;
            Inputs.Add(iv);
            RebuildTokens();
        }

        public DelegateCommand<InputVariable> RemoveInputCommand => new DelegateCommand<InputVariable>(RemoveInput);
        private void RemoveInput(InputVariable? item)
        {
            if (item != null)
            {
                item.PropertyChanged -= Input_PropertyChanged;
                Inputs.Remove(item);
                RebuildTokens();
            }
        }

        // 入力変数の名前が変わったら、式に使えるトークン一覧を作り直す
        private void Input_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(InputVariable.Name)) RebuildTokens();
        }

        // 制御式で使える積分変数(状態変数)のリスト。I制御などに使う。
        public ObservableCollection<StateVariable> States { get; } = new ObservableCollection<StateVariable>();

        public DelegateCommand AddStateCommand => new DelegateCommand(AddState);
        private void AddState()
        {
            var sv = new StateVariable($"int{States.Count + 1}", "", 100000);
            sv.PropertyChanged += State_PropertyChanged;
            States.Add(sv);
            RebuildTokens();
        }

        public DelegateCommand<StateVariable> RemoveStateCommand => new DelegateCommand<StateVariable>(RemoveState);
        private void RemoveState(StateVariable? item)
        {
            if (item != null)
            {
                item.PropertyChanged -= State_PropertyChanged;
                States.Remove(item);
                RebuildTokens();
            }
        }

        // 積分変数を手動で0に戻す
        public DelegateCommand<StateVariable> ResetStateCommand => new DelegateCommand<StateVariable>(
            sv => sv?.Reset());

        private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StateVariable.Name)) RebuildTokens();
        }

        // 制御式で使える微分変数のリスト。指定した式の d/dt を他の式で使える(D制御などに)。
        public ObservableCollection<DerivativeVariable> Derivatives { get; } = new ObservableCollection<DerivativeVariable>();

        public DelegateCommand AddDerivativeCommand => new DelegateCommand(AddDerivative);
        private void AddDerivative()
        {
            var dv = new DerivativeVariable($"dot{Derivatives.Count + 1}", "", 0.02);
            dv.PropertyChanged += Derivative_PropertyChanged;
            Derivatives.Add(dv);
            RebuildTokens();
        }

        public DelegateCommand<DerivativeVariable> RemoveDerivativeCommand => new DelegateCommand<DerivativeVariable>(RemoveDerivative);
        private void RemoveDerivative(DerivativeVariable? item)
        {
            if (item != null)
            {
                item.PropertyChanged -= Derivative_PropertyChanged;
                Derivatives.Remove(item);
                RebuildTokens();
            }
        }

        public DelegateCommand<DerivativeVariable> ResetDerivativeCommand => new DelegateCommand<DerivativeVariable>(
            dv => dv?.Reset());

        private void Derivative_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DerivativeVariable.Name)) RebuildTokens();
        }

        // 対応表(ルックアップテーブル)変数。ファイルの x-y 対応表から補間して値を出す。
        public ObservableCollection<TableVariable> Tables { get; } = new ObservableCollection<TableVariable>();

        public DelegateCommand AddTableCommand => new DelegateCommand(AddTable);
        private void AddTable()
        {
            var tv = new TableVariable($"map{Tables.Count + 1}", "");
            tv.PropertyChanged += Table_PropertyChanged;
            Tables.Add(tv);
            RebuildTokens();
        }

        public DelegateCommand<TableVariable> RemoveTableCommand => new DelegateCommand<TableVariable>(RemoveTable);
        private void RemoveTable(TableVariable? item)
        {
            if (item != null)
            {
                item.PropertyChanged -= Table_PropertyChanged;
                Tables.Remove(item);
                RebuildTokens();
            }
        }

        // 対応表ファイルを選んで読み込む
        public DelegateCommand<TableVariable> LoadTableFileCommand => new DelegateCommand<TableVariable>(LoadTableFile);
        private void LoadTableFile(TableVariable? item)
        {
            if (item == null) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "対応表 (*.csv;*.txt;*.tsv)|*.csv;*.txt;*.tsv|すべてのファイル (*.*)|*.*",
                Title = "対応表ファイルを選択 (1行につき x,y の2列)",
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    item.LoadFromFile(dialog.FileName);
                    ControlStatus = $"対応表「{item.Name}」に {item.PointCount} 点を読み込みました";
                }
                catch (Exception ex)
                {
                    ControlStatus = "対応表の読込に失敗: " + ex.Message;
                }
            }
        }

        public DelegateCommand<TableVariable> MoveTableUpCommand => new(x => { MoveItem(Tables, x, -1); RebuildTokens(); });
        public DelegateCommand<TableVariable> MoveTableDownCommand => new(x => { MoveItem(Tables, x, +1); RebuildTokens(); });

        private void Table_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TableVariable.Name)) RebuildTokens();
        }

        // 式変数(エイリアス): 名前=式 で登録し、他の式で名前として使える。式の記述を簡単にする。
        // 例) a = pos1+pos2 と登録すると、制御式などで a と書ける。
        // (MonitorItem を再利用: Name/Expression/Value/IsValid を持つ)
        public ObservableCollection<MonitorItem> Defines { get; } = new ObservableCollection<MonitorItem>();

        public DelegateCommand AddDefineCommand => new DelegateCommand(AddDefine);
        private void AddDefine()
        {
            var d = new MonitorItem($"a{Defines.Count + 1}", "");
            d.PropertyChanged += Define_PropertyChanged;
            Defines.Add(d);
            RebuildTokens();
        }

        public DelegateCommand<MonitorItem> RemoveDefineCommand => new DelegateCommand<MonitorItem>(RemoveDefine);
        private void RemoveDefine(MonitorItem? item)
        {
            if (item != null)
            {
                item.PropertyChanged -= Define_PropertyChanged;
                Defines.Remove(item);
                RebuildTokens();
            }
        }

        private void Define_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MonitorItem.Name)) RebuildTokens();
        }

        // ===== 並べ替え(上下移動) =====
        // ObservableCollection.Move を使うので、行の中身やイベント購読はそのまま保たれる。
        private static void MoveItem<T>(ObservableCollection<T> col, T? item, int delta)
        {
            if (item == null) return;
            int i = col.IndexOf(item);
            if (i < 0) return;
            int j = i + delta;
            if (j < 0 || j >= col.Count) return;
            col.Move(i, j);
        }

        public DelegateCommand<InputVariable> MoveInputUpCommand => new(x => { MoveItem(Inputs, x, -1); RebuildTokens(); });
        public DelegateCommand<InputVariable> MoveInputDownCommand => new(x => { MoveItem(Inputs, x, +1); RebuildTokens(); });
        public DelegateCommand<StateVariable> MoveStateUpCommand => new(x => { MoveItem(States, x, -1); RebuildTokens(); });
        public DelegateCommand<StateVariable> MoveStateDownCommand => new(x => { MoveItem(States, x, +1); RebuildTokens(); });
        public DelegateCommand<DerivativeVariable> MoveDerivativeUpCommand => new(x => { MoveItem(Derivatives, x, -1); RebuildTokens(); });
        public DelegateCommand<DerivativeVariable> MoveDerivativeDownCommand => new(x => { MoveItem(Derivatives, x, +1); RebuildTokens(); });
        public DelegateCommand<MonitorItem> MoveDefineUpCommand => new(x => { MoveItem(Defines, x, -1); RebuildTokens(); });
        public DelegateCommand<MonitorItem> MoveDefineDownCommand => new(x => { MoveItem(Defines, x, +1); RebuildTokens(); });
        public DelegateCommand<MonitorItem> MoveMonitorUpCommand => new(x => MoveItem(Monitors, x, -1));
        public DelegateCommand<MonitorItem> MoveMonitorDownCommand => new(x => MoveItem(Monitors, x, +1));

        // 安全のための目標電流の絶対値上限(生値)。電流(トルク)モードの式結果をこの範囲にクランプする。
        private int _ControlCurrentLimit = 300;
        public int ControlCurrentLimit
        {
            get => _ControlCurrentLimit;
            set
            {
                int clamped = Math.Max(0, Math.Min(short.MaxValue, value));
                if (_ControlCurrentLimit != clamped) { _ControlCurrentLimit = clamped; NotifyPropertyChanged(); }
            }
        }

        // 速度モードの目標速度の絶対値上限(生値)。速度モードの式結果をこの範囲にクランプする。
        private int _ControlVelocityLimit = 1023;
        public int ControlVelocityLimit
        {
            get => _ControlVelocityLimit;
            set
            {
                int clamped = Math.Max(0, value);
                if (_ControlVelocityLimit != clamped) { _ControlVelocityLimit = clamped; NotifyPropertyChanged(); }
            }
        }

        private bool _IsControlRunning = false;
        public bool IsControlRunning
        {
            get => _IsControlRunning;
            private set { if (_IsControlRunning != value) { _IsControlRunning = value; NotifyPropertyChanged(); } }
        }

        // ポーリング兼制御ループの目標周期(ms)。小さいほど速い。最低2msでガードする。
        // 実効周期は「この値 + 1周期ぶんの通信時間」になる。
        private int _ControlLoopDelayMs = 10;
        public int ControlLoopDelayMs
        {
            get => _ControlLoopDelayMs;
            set
            {
                int clamped = Math.Max(2, Math.Min(1000, value));
                if (_ControlLoopDelayMs != clamped) { _ControlLoopDelayMs = clamped; NotifyPropertyChanged(); }
            }
        }

        private string _ControlStatus = "停止中";
        public string ControlStatus
        {
            get => _ControlStatus;
            set { if (_ControlStatus != value) { _ControlStatus = value; NotifyPropertyChanged(); } }
        }

        // ループ(=制御)の実測周波数[Hz]。一定時間ごとの実行回数から算出する。
        private double _MeasuredHz = 0;
        public double MeasuredHz
        {
            get => _MeasuredHz;
            private set { if (_MeasuredHz != value) { _MeasuredHz = value; NotifyPropertyChanged(); } }
        }

        // 監視する変数のリスト(名前+式)。式の評価結果がGUI上でライブ更新される。
        public ObservableCollection<MonitorItem> Monitors { get; } = new ObservableCollection<MonitorItem>();

        // GUIで式を組み立てる際にクリックで挿入できる変数トークンの一覧
        public ObservableCollection<string> AvailableTokens { get; } = new ObservableCollection<string>();

        // 派生変数(式変数・微分変数・監視変数)の「前周期の値」。
        // 式変数と微分変数が相互参照する場合(例: dela←delar, delb←be)に、まだ今周期で
        // 計算されていない相手を前周期の値で解決するためのフィードバック。これでハード失敗を無くす。
        private readonly Dictionary<string, double> _lastDerived =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // 変数名(小文字) -> 最新値 のスナップショットを作る。制御式の評価に使う。
        private Dictionary<string, double> BuildVariableSnapshot()
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            // 入力変数(ゲイン等)を先に入れる
            foreach (var iv in Inputs.ToList())
            {
                if (!string.IsNullOrWhiteSpace(iv.Name))
                    dict[iv.Name] = iv.Value;
            }
            // 積分変数(状態変数)の現在値
            foreach (var sv in States.ToList())
            {
                if (!string.IsNullOrWhiteSpace(sv.Name))
                    dict[sv.Name] = sv.Value;
            }
            // モーターの位置・速度・電流
            foreach (var m in Motors.ToList())
            {
                dict[$"pos{m.Id}"] = m.NowValue - m.PositionZeroOffset; // 仮想ゼロ点を差し引く
                dict[$"vel{m.Id}"] = m.PresentVelocity;
                dict[$"cur{m.Id}"] = m.PresentCurrent;
            }
            return dict;
        }

        // クリック挿入用トークン一覧を、現在のモーター構成から作り直す
        public void RebuildTokens()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AvailableTokens.Clear();
                // 入力変数(名前)を先に
                foreach (var iv in Inputs)
                {
                    if (!string.IsNullOrWhiteSpace(iv.Name))
                        AvailableTokens.Add(iv.Name);
                }
                // 積分変数(名前)
                foreach (var sv in States)
                {
                    if (!string.IsNullOrWhiteSpace(sv.Name))
                        AvailableTokens.Add(sv.Name);
                }
                // 微分変数(名前)
                foreach (var dv in Derivatives)
                {
                    if (!string.IsNullOrWhiteSpace(dv.Name))
                        AvailableTokens.Add(dv.Name);
                }
                // 対応表変数(名前)
                foreach (var tv in Tables)
                {
                    if (!string.IsNullOrWhiteSpace(tv.Name))
                        AvailableTokens.Add(tv.Name);
                }
                // 式変数(エイリアス)の名前
                foreach (var d in Defines)
                {
                    if (!string.IsNullOrWhiteSpace(d.Name))
                        AvailableTokens.Add(d.Name);
                }
                // モーターの位置・速度・電流
                foreach (var m in Motors)
                {
                    AvailableTokens.Add($"pos{m.Id}");
                    AvailableTokens.Add($"vel{m.Id}");
                    AvailableTokens.Add($"cur{m.Id}");
                }
            });
        }

        /// <summary>
        /// ポーリングループ(裏スレッド)から毎周期呼ばれる。dtは前回からの実経過時間(秒)。
        /// 監視変数を評価してGUIに反映し、制御実行中なら積分変数を更新して各対象モーターの
        /// トルクを計算・送信する。
        /// </summary>
        private void RunControlStep(double dt)
        {
            // 基本変数(入力・積分・モーターpos/vel/cur)。これらは今周期の確定値で最優先。
            Dictionary<string, double> baseSnap = BuildVariableSnapshot();
            var baseNames = new HashSet<string>(baseSnap.Keys, StringComparer.OrdinalIgnoreCase);

            // スナップショット: まず前周期の派生値を低優先度で入れ、その上に基本変数を被せる。
            // これで「まだ今周期で計算していない派生変数」を参照しても前周期の値で解決でき、
            // 式変数⇄微分変数の相互参照でも評価が失敗しない(相互依存は最大1周期の遅れになる)。
            var snapshot = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _lastDerived) snapshot[kv.Key] = kv.Value;
            foreach (var kv in baseSnap) snapshot[kv.Key] = kv.Value;

            double Resolve(string name)
            {
                if (snapshot.TryGetValue(name, out double v)) return v;
                throw new KeyNotFoundException(name);
            }

            // 派生変数を確定する。基本変数(予約名)は上書きしない。今周期値を次周期用に記録する。
            void StoreDerived(string name, double val)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                _lastDerived[name] = val;
                if (!baseNames.Contains(name)) snapshot[name] = val; // 派生は上書きして下流へ伝える
            }

            // 式変数(エイリアス)をリスト順に評価。上の行の式変数を下の行から参照できる。
            foreach (var def in Defines.ToList())
            {
                var expr = def.Compiled;
                if (expr != null && expr.TryEvaluate(Resolve, out double dv))
                {
                    def.Value = dv;
                    StoreDerived(def.Name, dv);
                }
            }

            // 微分変数を更新: 対象式を評価して d/dt を計算し、注入する。
            if (dt > 0)
            {
                foreach (var der in Derivatives.ToList())
                {
                    var expr = der.Expression;
                    if (expr != null && expr.IsValid && expr.TryEvaluate(Resolve, out double x))
                    {
                        der.Update(x, dt);
                        StoreDerived(der.Name, der.Value);
                    }
                }
            }

            // 対応表変数を評価: x(式)を評価し、対応表から補間した y を注入する。
            // 式変数・微分変数の後に置くことで、それらを x に使える。
            foreach (var tv in Tables.ToList())
            {
                if (!tv.HasTable) continue;
                var xexpr = tv.XExpression;
                if (xexpr != null && xexpr.IsValid && xexpr.TryEvaluate(Resolve, out double x))
                {
                    double y = tv.Lookup(x);
                    tv.Value = y;
                    StoreDerived(tv.Name, y);
                }
            }

            // 監視変数を評価(制御停止中でも値が見える)。監視値も他の式から参照できるようにする。
            foreach (var mon in Monitors.ToList())
            {
                var expr = mon.Compiled;
                if (expr != null && expr.TryEvaluate(Resolve, out double val))
                {
                    mon.Value = val;
                    StoreDerived(mon.Name, val);
                }
            }

            if (!IsControlRunning) return;

            // 積分変数を更新する: 被積分の式を(積分前の測定値で)評価し、value += 値×dt。
            // 更新後の値を snapshot にも反映し、下の制御式が最新の積分値を参照できるようにする。
            if (dt > 0)
            {
                foreach (var sv in States.ToList())
                {
                    if (string.IsNullOrWhiteSpace(sv.Name)) continue;
                    var integ = sv.Integrand;
                    if (integ != null && integ.IsValid && integ.TryEvaluate(Resolve, out double rate))
                    {
                        sv.Integrate(rate, dt);
                        snapshot[sv.Name] = sv.Value; // 制御式が最新の積分値を見られるように上書き
                    }
                }
            }

            // 各対象モーターの指令値を計算し、出力モード別にまとめてGroupSyncWriteで送る
            var curGoals = new Dictionary<byte, int>(); // 電流(トルク)
            var velGoals = new Dictionary<byte, int>(); // 速度
            var posGoals = new Dictionary<byte, int>(); // 位置/拡張位置
            foreach (var motor in Motors.ToList())
            {
                if (!IsControlRunning) return;             // STOP中は送信しない
                if (!motor.IsControlled) continue;
                if (!motor.IsEnable) continue;             // トルクOFF中のモーターには指令しない(手動OFFを尊重)
                var expr = motor.ControlExpression;
                if (expr == null || !expr.IsValid) continue;

                if (expr.TryEvaluate(Resolve, out double outVal))
                {
                    int goal = (int)Math.Round(outVal);
                    switch (motor.ControlModeIndex)
                    {
                        case 1: // 速度
                            goal = Math.Max(-ControlVelocityLimit, Math.Min(ControlVelocityLimit, goal));
                            velGoals[motor.MotorId] = goal;
                            break;
                        case 2: // 位置
                        case 3: // 拡張位置
                            // 制御式は仮想ゼロ基準(pos = 生値 - offset)で計算しているので、
                            // モーターへ書く目標位置はオフセットを足し戻して生の位置に変換する
                            goal = goal + motor.PositionZeroOffset;
                            goal = Math.Max(motor.SliderMin, Math.Min(motor.SliderMax, goal));
                            posGoals[motor.MotorId] = goal;
                            break;
                        default: // 電流(トルク)
                            goal = Math.Max(-ControlCurrentLimit, Math.Min(ControlCurrentLimit, goal));
                            curGoals[motor.MotorId] = goal;
                            break;
                    }
                    motor.ControlOutput = goal;
                }
            }

            if (curGoals.Count > 0) _dynamixelModel.SetGoalCurrentsSync(curGoals);
            if (velGoals.Count > 0) _dynamixelModel.SetGoalVelocitiesSync(velGoals);
            if (posGoals.Count > 0) _dynamixelModel.SetGoalPositionsSync(posGoals);
        }

        public DelegateCommand AddMonitorCommand => new DelegateCommand(AddMonitor);
        private void AddMonitor()
        {
            Monitors.Add(new MonitorItem($"var{Monitors.Count + 1}", ""));
        }

        public DelegateCommand<MonitorItem> RemoveMonitorCommand => new DelegateCommand<MonitorItem>(RemoveMonitor);
        private void RemoveMonitor(MonitorItem? item)
        {
            if (item != null) Monitors.Remove(item);
        }

        // 全モーターの現在地を仮想ゼロ点にする / 解除する
        public DelegateCommand ZeroAllPositionsCommand => new DelegateCommand(() =>
        {
            foreach (var m in Motors) m.PositionZeroOffset = m.NowValue;
            ControlStatus = "全モーターの現在地を0基準にしました";
        });
        public DelegateCommand ClearAllZeroCommand => new DelegateCommand(() =>
        {
            foreach (var m in Motors) m.PositionZeroOffset = 0;
            ControlStatus = "仮想ゼロ点を解除しました";
        });

        // 制御モードの出力種別(0=電流,1=速度,2=位置,3=拡張位置) を Dynamixel の動作モードへ変換
        private static byte OperatingModeFor(int controlModeIndex) => controlModeIndex switch
        {
            1 => DynamixelModel.OP_MODE_VELOCITY,
            2 => DynamixelModel.OP_MODE_POSITION,
            3 => DynamixelModel.OP_MODE_EXT_POSITION,
            _ => DynamixelModel.OP_MODE_CURRENT,
        };

        public DelegateCommand StartControlCommand => new DelegateCommand(StartControl);
        private void StartControl()
        {
            if (!_dynamixelModel.IsConnected)
            {
                ControlStatus = "先にUSB接続してください";
                return;
            }

            var targets = Motors.Where(m => m.IsControlled).ToList();
            if (targets.Count == 0)
            {
                ControlStatus = "制御対象のモーターを選んでください";
                return;
            }
            var invalid = targets.Where(m => m.ControlExpression == null || !m.ControlExpression.IsValid).ToList();
            if (invalid.Count > 0)
            {
                ControlStatus = $"モーター {string.Join(",", invalid.Select(m => m.Id))} の制御式が無効です";
                return;
            }

            ControlStatus = "制御モードへ切替中...";
            _controlStartedMotors = targets; // STOP時に確実にトルクOFFできるよう、起動した対象を覚えておく

            // 積分変数・微分変数をリセット(前回の溜まり/前回値が残っていると起動直後に飛ぶため)
            foreach (var sv in States) sv.Reset();
            foreach (var dv in Derivatives) dv.Reset();

            // モーターごとの (id, 出力モード, 現在位置) を先に取り出しておく
            var setup = targets.Select(m => (id: m.MotorId, mode: m.ControlModeIndex, pos: m.NowValue)).ToList();

            Task.Run(() =>
            {
                // 動作モードはトルクOFFのときしか書き換えられないので順番を守る
                foreach (var (id, modeIdx, pos) in setup)
                {
                    byte opMode = OperatingModeFor(modeIdx);
                    _dynamixelModel.SetTorqueEnable(id, false);
                    _dynamixelModel.SetOperatingMode(id, opMode);
                    // 起動直後に飛ばないよう初期指令を安全側に置く
                    if (opMode == DynamixelModel.OP_MODE_CURRENT) _dynamixelModel.SetGoalCurrent(id, 0);
                    else if (opMode == DynamixelModel.OP_MODE_VELOCITY) _dynamixelModel.SetGoalVelocity(id, 0);
                    else _dynamixelModel.SetGoalPosition(id, pos); // 位置モードは現在位置で保持
                    _dynamixelModel.SetTorqueEnable(id, true);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var motor in targets) motor.IsEnable = true;
                    IsControlRunning = true;
                    ControlStatus = $"制御実行中 ({targets.Count}台)";
                });
            });
        }

        // 制御を開始した対象モーター(STOP時に確実にトルクOFFするために保持)
        private List<MotorViewModel> _controlStartedMotors = new();

        public DelegateCommand StopControlCommand => new DelegateCommand(StopControl);
        private void StopControl()
        {
            // 1. まず制御ループを止める。これでポーリングループは電流を書き込まなくなる。
            IsControlRunning = false;

            // 2. 起動した対象＋現在の制御対象(取りこぼし防止でtoggle済みも含める)を集める
            var targets = _controlStartedMotors
                .Union(Motors.Where(m => m.IsControlled))
                .Distinct()
                .ToList();
            foreach (var m in targets) m.ControlOutput = 0;
            byte[] ids = targets.Select(m => m.MotorId).ToArray();

            // 3. トルクOFFを「1本のタスクで順番に」実行する。
            //    複数タスクやIsEnableセッターと競合させないことで、OFFパケットの取りこぼしを防ぐ。
            //    さらに念押しでもう一度OFFを送る(混雑時の保険)。
            Task.Run(() =>
            {
                foreach (byte id in ids)
                {
                    _dynamixelModel.SetGoalCurrent(id, 0);   // まず電流指令を0に
                    _dynamixelModel.SetTorqueEnable(id, false);
                }
                foreach (byte id in ids)
                    _dynamixelModel.SetTorqueEnable(id, false); // 念押しのOFF

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // ハード側は上で確実にOFF済みなので、UI表示だけ合わせる(二重送信を避ける)
                    foreach (var m in targets) m.SetEnableStateSilently(false);
                    ControlStatus = "制御停止 (トルクOFF)";
                });
            });

            _controlStartedMotors = new();
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
            int baudRate = SelectedBaudRate; // 画面で選んだボーレートを使う

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

        // 選択できるボーレート一覧。既定は57600(モーターの工場出荷設定)。
        // 高い値にする場合は、モーター側も DYNAMIXEL Wizard 等で同じ値に設定しておく必要がある。
        public int[] AvailableBaudRates { get; } = new[] { 57600, 115200, 1000000, 2000000, 3000000, 4000000 };

        private int _SelectedBaudRate = 57600;
        public int SelectedBaudRate
        {
            get => _SelectedBaudRate;
            set { if (_SelectedBaudRate != value) { _SelectedBaudRate = value; NotifyPropertyChanged(); } }
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
            // スクリプトループ・制御ループを止める
            _IsScriptRunning = false;
            IsControlRunning = false;

            // 全モーターのトルクを「状態に関係なく無条件で」OFFにする。
            // ※ IsEnableセッター経由だと「値が変わった時だけ」しか送らないため、
            //   アプリ側の状態が実機とズレているとOFFが1本も飛ばないことがある。
            //   ここでは1本のタスクで順番に、さらに念押しで2回送って確実に止める。
            var motors = Motors.ToList();
            byte[] ids = motors.Select(m => m.MotorId).ToArray();
            Task.Run(() =>
            {
                foreach (byte id in ids)
                    _dynamixelModel.SetTorqueEnable(id, false);
                foreach (byte id in ids)      // 混雑時の取りこぼし対策で念押し
                    _dynamixelModel.SetTorqueEnable(id, false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // ハード側は上で確実にOFF済みなので、UI表示だけ合わせる(二重送信を避ける)
                    foreach (var m in motors) m.SetEnableStateSilently(false);
                    ConnectionStatus = "緊急停止しました(トルクOFF)";
                });
            });
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