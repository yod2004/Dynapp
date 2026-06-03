using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace Dynapp
{
    public class DelegateCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        // ボタンが押されたときに実行する処理（Action）を登録する
        public DelegateCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // ボタンが押せるかどうかを判定する（今回は常にtrue）
        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        // ボタンを押したときに実際に実行される処理
        public void Execute(object? parameter) => _execute();

        // ボタンの有効・無効状態が変わったことをWPFに伝えるイベント
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
