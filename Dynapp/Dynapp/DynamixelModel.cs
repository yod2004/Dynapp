using System;
using System.Collections.Generic;
using System.Text;
using dynamixel_sdk;
using System.Collections.Generic;

namespace Dynapp
{
    public class DynamixelModel
    {
        private const int ADDR_TORQUE_ENABLE = 64; // 現在位置のアドレス
        private const int ADDR_X_LED = 65;
        private const int PROTOCOL_VERSION = 2;
        private const int ADDR_GOAL_POSITION = 116;
        private const int ADDR_PRESENT_POSITION = 132; // 現在位置のアドレス
        private const int ADDR_PRESENT_CURRENT = 126; // 電流値のアドレス (Xシリーズ想定)
        private const int LEN_PRESENT_POSITION = 4; // 現在位置のデータ長（4byte）
        private const int LEN_PRESENT_CURRENT = 2;    // 電流値のデータ長（2byte）
        private int _portNum = -1;
        private readonly object _lockObj = new object();
        private int _groupSyncReadNum = -1;
        private int _groupSyncReadCurrentNum = -1;

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
            _groupSyncReadNum = Dynamixel.groupSyncRead(_portNum, PROTOCOL_VERSION, ADDR_PRESENT_POSITION, LEN_PRESENT_POSITION);
            _groupSyncReadCurrentNum = Dynamixel.groupSyncRead(_portNum, PROTOCOL_VERSION, ADDR_PRESENT_CURRENT, LEN_PRESENT_CURRENT);
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

        /// <summary>
        /// 複数のモーターの現在位置を一括で取得する (GroupSyncRead)
        /// </summary>
        public Dictionary<byte, int> ReadAllPositions(byte[] motorIds)
        {
            // 未接続、またはグループが作られていなければ null を返す
            if (_portNum == -1 || _groupSyncReadNum == -1) return null;

            lock (_lockObj)
            {
                // 1. 前回の通信の登録リストをリセット
                Dynamixel.groupSyncReadClearParam(_groupSyncReadNum);

                // 2. 今回読み取りたいモーターのIDをリストに追加
                foreach (byte id in motorIds)
                {
                    Dynamixel.groupSyncReadAddParam(_groupSyncReadNum, id);
                }

                // 3. 全員に向けて「現在位置を教えろ！」と一斉送信＆一括受信（通信はこれ1回だけ！）
                Dynamixel.groupSyncReadTxRxPacket(_groupSyncReadNum);

                // 4. 受け取った結果を辞書（ID -> 現在位置）にまとめる
                var results = new Dictionary<byte, int>();
                foreach (byte id in motorIds)
                {
                    // データが正しく届いているか確認
                    if (Dynamixel.groupSyncReadIsAvailable(_groupSyncReadNum, id, ADDR_PRESENT_POSITION, LEN_PRESENT_POSITION))
                    {
                        results[id] = (int)Dynamixel.groupSyncReadGetData(_groupSyncReadNum, id, ADDR_PRESENT_POSITION, LEN_PRESENT_POSITION);
                    }
                }
                return results;
            }
        }

        /// <summary>
        /// 複数のモーターの現在電流値(mA)を一括で取得する (GroupSyncRead)
        /// </summary>
        public Dictionary<byte, double> ReadAllCurrents(byte[] motorIds)
        {
            // 未接続、またはグループが作られていなければ null を返す
            if (_portNum == -1 || _groupSyncReadCurrentNum == -1) return null;

            lock (_lockObj)
            {
                // 1. 前回の通信の登録リストをリセット
                Dynamixel.groupSyncReadClearParam(_groupSyncReadCurrentNum);

                // 2. 今回読み取りたいモーターのIDをリストに追加
                foreach (byte id in motorIds)
                {
                    Dynamixel.groupSyncReadAddParam(_groupSyncReadCurrentNum, id);
                }

                // 3. 全員に向けて「現在電流を教えろ！」と一斉送信＆一括受信
                Dynamixel.groupSyncReadTxRxPacket(_groupSyncReadCurrentNum);

                // 4. 受け取った結果を辞書（ID -> 電流値mA）にまとめる
                var results = new Dictionary<byte, double>();
                foreach (byte id in motorIds)
                {
                    // データが正しく届いているか確認
                    if (Dynamixel.groupSyncReadIsAvailable(_groupSyncReadCurrentNum, id, ADDR_PRESENT_CURRENT, LEN_PRESENT_CURRENT))
                    {
                        // SDKからは uint でデータが返ってくるため、2バイト符号付き整数(short)にキャストする
                        // これにより、正負の負荷（正転・逆転方向への負荷電流）が正しく表現されます
                        uint rawData = Dynamixel.groupSyncReadGetData(_groupSyncReadCurrentNum, id, ADDR_PRESENT_CURRENT, LEN_PRESENT_CURRENT);
                        short currentRaw = (short)rawData;

                        // 生データ(RAW)を実際の電流単位(mA)に変換
                        // ※ XM430やXC430などのXシリーズは 1単位 = 2.69mA です
                        double currentmA = currentRaw * 2.69;

                        results[id] = currentmA;
                    }
                }
                return results;
            }
        }
    }
}