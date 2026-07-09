using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;

namespace Dynapp
{
    public class MotorViewModel : INotifyPropertyChanged
    {
        private readonly DynamixelModel _dynamixelModel;

        // 自分のモーターIDを保持する
        public byte MotorId => (byte)Id;

        // コンストラクタでIDと通信モデルを受け取る
        public MotorViewModel(byte id, DynamixelModel dynamixelModel)
        {
            _Id = id;
            _dynamixelModel = dynamixelModel;
        }

        private int _Id;
        public int Id
        {
            get { return _Id; }
            set
            {
                if (_Id != value)
                {
                    _Id = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private bool _IsLed;
        public bool IsLed
        {
            get => _IsLed;
            set
            {
                if (_IsLed != value)
                {
                    _IsLed = value;
                    NotifyPropertyChanged();
                    Task.Run(() => _dynamixelModel.SetLed(MotorId, value)); // 自分のIDを使う！
                }
            }
        }

        private bool _IsEnable;
        public bool IsEnable
        {
            get => _IsEnable;
            set
            {
                if (_IsEnable != value)
                {
                    _IsEnable = value;
                    NotifyPropertyChanged();
                    Task.Run(()=>_dynamixelModel.SetTorqueEnable(MotorId, value)); // 自分のIDを使う！
                }
            }
        }

        private int _NowValue = 0;
        public int NowValue
        {
            get => _NowValue;
            set
            {
                if (_NowValue != value)
                {
                    _NowValue = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private int _TargetValue = 0;
        public int TargetValue
        {
            get => _TargetValue;
            set
            {
                if (_TargetValue != value)
                {
                    _TargetValue = value;
                    NotifyPropertyChanged();
                    Task.Run(()=>_dynamixelModel.SetGoalPosition(MotorId, value)); // 自分のIDを使う！
                }
            }
        }

        private int _SliderValue = 0;
        public int SliderValue
        {
            get => _SliderValue;
            set
            {
                if (_SliderValue != value)
                {
                    _SliderValue = value;
                    NotifyPropertyChanged();
                    TargetValue = value;
                }
            }

        }

        // --- Position PID ゲイン (UIから調整可能) ---
        private int _PositionPGain = 800;
        public int PositionPGain
        {
            get => _PositionPGain;
            set
            {
                if (_PositionPGain != value)
                {
                    _PositionPGain = value;
                    NotifyPropertyChanged();
                    Task.Run(() => _dynamixelModel.SetPositionPGain(MotorId, (ushort)Math.Clamp(value, 0, 16383)));
                }
            }
        }

        private int _PositionIGain = 0;
        public int PositionIGain
        {
            get => _PositionIGain;
            set
            {
                if (_PositionIGain != value)
                {
                    _PositionIGain = value;
                    NotifyPropertyChanged();
                    Task.Run(() => _dynamixelModel.SetPositionIGain(MotorId, (ushort)Math.Clamp(value, 0, 16383)));
                }
            }
        }

        private int _PositionDGain = 0;
        public int PositionDGain
        {
            get => _PositionDGain;
            set
            {
                if (_PositionDGain != value)
                {
                    _PositionDGain = value;
                    NotifyPropertyChanged();
                    Task.Run(() => _dynamixelModel.SetPositionDGain(MotorId, (ushort)Math.Clamp(value, 0, 16383)));
                }
            }
        }

        /// <summary>
        /// モーターから現在のPIDゲインを読み取ってUIに反映する（送信はせず表示だけ更新）
        /// </summary>
        public void RefreshGainsFromMotor()
        {
            var (p, i, d) = _dynamixelModel.ReadPositionGains(MotorId);
            // setter経由だと書き戻しが走るのでフィールドを直接更新する
            _PositionPGain = p; NotifyPropertyChanged(nameof(PositionPGain));
            _PositionIGain = i; NotifyPropertyChanged(nameof(PositionIGain));
            _PositionDGain = d; NotifyPropertyChanged(nameof(PositionDGain));
        }

        // --- 作動機構テスト(速度制御)用 ---

        // このモーターを速度連動の対象にするか
        private bool _IsLinked;
        public bool IsLinked
        {
            get => _IsLinked;
            set
            {
                if (_IsLinked != value)
                {
                    _IsLinked = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // 回転方向を反転させるか（連動グループ内で他のモーターと逆回転させたいとき）
        private bool _IsReversed;
        public bool IsReversed
        {
            get => _IsReversed;
            set
            {
                if (_IsReversed != value)
                {
                    _IsReversed = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // 現在このモーターに出している目標速度（表示用）
        private int _GoalVelocity = 0;
        public int GoalVelocity
        {
            get => _GoalVelocity;
            set
            {
                if (_GoalVelocity != value)
                {
                    _GoalVelocity = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private int _ModeIndex = 3;
        public int ModeIndex
        {
            get => _ModeIndex;
            set
            {
                if (_ModeIndex != value)
                {
                    _ModeIndex = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // Extended Position(マルチターン)モードのGoal Position下限 (約-256回転)
        private int _SliderMin = -1048575;
        public int SliderMin
        {
            get => _SliderMin;
            set
            {
                if (_SliderMin != value)
                {
                    _SliderMin = value;
                    NotifyPropertyChanged();
                }
            }
        }

        // Extended Position(マルチターン)モードのGoal Position上限 (約+256回転)
        private int _SliderMax = 1048575;
        public int SliderMax
        {
            get => _SliderMax;
            set
            {
                if (_SliderMax != value)
                {
                    _SliderMax = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private bool _IsShowGraph = true;
        public bool IsShowGraph
        {
            get => _IsShowGraph;
            set
            {
                if (_IsShowGraph != value)
                {
                    _IsShowGraph = value;
                    NotifyPropertyChanged();
                    if (value)
                    {
                        IsShowCurrentGraph = true;
                        IsShowPositionGraph = true;
                    }
                    else
                    {
                        IsShowCurrentGraph = false;
                        IsShowPositionGraph =false;
                    }
                }
            }
        }

        private bool _IsShowCurrentGraph = true;
        public bool IsShowCurrentGraph
        {
            get => _IsShowCurrentGraph;
            set
            {
                if (_IsShowCurrentGraph != value)
                {
                    _IsShowCurrentGraph = value;
                    NotifyPropertyChanged();
                }
            }
        }
        private bool _IsShowPositionGraph = true;
        public bool IsShowPositionGraph
        {
            get => _IsShowPositionGraph;
            set
            {
                if (_IsShowPositionGraph != value)
                {
                    _IsShowPositionGraph = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private ushort _ModelNumber = 0;
        public ushort ModelNumber
        {
            get => _ModelNumber;
            set
            {
                if (_ModelNumber != value)
                {
                    _ModelNumber = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(CurrentScale)); // 値が変わったらスケールも更新
                    NotifyPropertyChanged(nameof(ModelName));    // 画面表示用の名前も更新
                }
            }
        }
        // ★ モデル番号から変換係数を自動で決定する
        public double CurrentScale
        {
            get
            {
                // XM430, XH430, XM540 などのシリーズ（モデル番号 1000 ～ 1150付近）は 1単位 = 2.69mA
                if (ModelNumber >= 1000 && ModelNumber <= 1150)
                {
                    return 2.69;
                }

                // XC330シリーズ (1210 ~ 1240) などは 1単位 = 1.0mA
                // XL330は元々Current制御ができませんが、取得できた場合はそのまま1.0として扱います
                return 1.0;
            }
        }
        public string ModelName
        {
            get
            {
                switch (ModelNumber)
                {
                    case 1000: return "XH430-W350"; // 修正
                    case 1010: return "XH430-W210"; // 修正
                    case 1020: return "XM430-W350"; // 修正
                    case 1030: return "XM430-W210"; // 修正
                    case 1040: return "XH430-V350"; // 念のためVシリーズも追加
                    case 1050: return "XH430-V210";
                    case 1060: return "XL430-W250";

                    case 1120: return "XM540-W150";
                    case 1130: return "XM540-W270";

                    case 1190: return "XL330-M288";
                    case 1200: return "XL330-M077";

                    case 1210: return "XC330-T181";
                    case 1220: return "XC330-T288";
                    case 1230: return "XC330-M181";
                    case 1240: return "XC330-M288";

                    case 0: return "Unknown";
                    default: return $"Model:{ModelNumber}";
                }
            }
        }

        // --- MotorViewModel.cs の中に追加 ---

        // モーターから現在のPIDゲインを読み込むコマンド
        public DelegateCommand RefreshGainsCommand => new DelegateCommand(
            () => Task.Run(() => RefreshGainsFromMotor()));

        // 目標値を0に戻すコマンド（モーターを原点位置へ）
        public DelegateCommand GoToZeroCommand => new DelegateCommand(GoToZero);

        private void GoToZero()
        {
            // SliderValue 経由で設定すると TargetValue → SetGoalPosition まで連動する
            SliderValue = 0;
            // 既に目標値が0だった場合 setter の変更ガードで送信がスキップされるため、確実に再送する
            Task.Run(() => _dynamixelModel.SetGoalPosition(MotorId, 0));
        }

        public DelegateCommand RebootCommand => new DelegateCommand(RebootMotor);

        private void RebootMotor()
        {
            // UIをフリーズさせないよう、裏側スレッドで実行
            Task.Run(async () =>
            {
                // Modelに再起動を指示
                _dynamixelModel.Reboot(MotorId);

                // 再起動には少し時間がかかるので少し待機
                await Task.Delay(500);

                // ハードウェアはトルクOFF状態に戻っているので、画面のチェックボックスも連動して外す
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsEnable = false;
                });
            });
        }

        // --- INotifyPropertyChanged の実装（お決まりのコード） ---
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
