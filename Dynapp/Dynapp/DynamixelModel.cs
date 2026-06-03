using System;
using System.Collections.Generic;
using System.Text;
using dynamixel_sdk;

namespace Dynapp
{
    public class DynamixelModel
    {
        private const int ADDR_TORQUE_ENABLE = 64; // 現在位置のアドレス
        private const int ADDR_X_LED = 65;
        private const int PROTOCOL_VERSION = 2;
        private const int ADDR_GOAL_POSITION = 116;
        private const int ADDR_PRESENT_POSITION = 132; // 現在位置のアドレス
        private int _portNum = -1;
        private readonly object _lockObj = new object();

        /// <summary>
        /// 指定したCOMポートとボーレートでDynamixelと接続する
        /// </summary>
        public bool Connect(string portName, int baudRate)
        {
            // 1. ポートハンドラーの初期化
            _portNum = Dynamixel.portHandler(portName);

            // 2. パケットハンドラーの初期化
            Dynamixel.packetHandler();

            // 3. ポートを開く
            if (!Dynamixel.openPort(_portNum))
            {
                System.Diagnostics.Debug.WriteLine("ポートを開けませんでした。");
                return false;
            }

            // 4. ボーレート（通信速度）を設定する
            if (!Dynamixel.setBaudRate(_portNum, baudRate))
            {
                System.Diagnostics.Debug.WriteLine("ボーレートの設定に失敗しました。");
                // 失敗したらポートを閉じておく
                Dynamixel.closePort(_portNum);
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"接続成功: {portName} ({baudRate} bps)");
            return true;
        }

        /// <summary>
        /// アプリ終了時などにポートを閉じる処理
        /// </summary>
        public void Disconnect()
        {
            if (_portNum != -1)
            {
                Dynamixel.closePort(_portNum);
                _portNum = -1;
            }
        }

        // LEDを制御する純粋なロジック
        public void SetLed(byte motorId, bool turnOn)
        {
            if(_portNum == -1) return; // 未接続なら何もしない
            byte ledValue = (byte)(turnOn ? 1 : 0);
            lock (_lockObj)
            {
                Dynamixel.write1ByteTxRx(_portNum, PROTOCOL_VERSION, motorId, ADDR_X_LED, ledValue);
            }
        }

        public void SetTorqueEnable(byte motorId, bool enable)
        {
            if(_portNum == -1) return; // 未接続なら何もしない
            byte enableValue = (byte)(enable ? 1 : 0);
            lock (_lockObj)
            {
               Dynamixel.write1ByteTxRx(_portNum, PROTOCOL_VERSION, motorId, ADDR_TORQUE_ENABLE, enableValue);
            }
        }

        public void SetGoalPosition(byte motorId, int target)
        {
            if (_portNum == -1) return; // 未接続なら何もしない
            lock (_lockObj)
            {
                Dynamixel.write4ByteTxRx(_portNum, PROTOCOL_VERSION, motorId, ADDR_GOAL_POSITION, (uint)target);
            }
        }

        /// <summary>
        /// 指定したIDの現在位置を読み取る
        /// </summary>
        public int GetPresentPosition(byte motorId)
        {
            if (_portNum == -1) return 0; // 未接続なら0を返す
            lock (_lockObj)
            {
                // 4バイト読み込みの関数を使う
                uint presentPosition = Dynamixel.read4ByteTxRx(_portNum, PROTOCOL_VERSION, motorId, ADDR_PRESENT_POSITION);
            
                // dynamixelの関数は符号なし(uint)で返してくるので、intに変換して返す
                return (int)presentPosition;
            }
        }
    }
}