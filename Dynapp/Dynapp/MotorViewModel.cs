using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

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

        private int _SliderMin = -4095;
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

        private int _SliderMax = 4095;
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

        // --- INotifyPropertyChanged の実装（お決まりのコード） ---
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
