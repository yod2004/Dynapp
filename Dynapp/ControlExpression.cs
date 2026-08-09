using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dynapp
{
    /// <summary>
    /// GUIで組み立てた制御式(文字列)を解析して数値評価する、依存ライブラリ不要の小さな式エンジン。
    ///
    /// 対応:
    ///   四則演算 + - * /、剰余 %、べき乗 ^ (右結合)、単項マイナス、丸括弧 ( )
    ///   変数(識別子): 例) k, d, pos1, vel2, cur3 など。値は評価時にresolverで解決する。
    ///   関数: abs, sign, min, max, clamp, sqrt, sin, cos, tan, exp, log, deg, rad
    ///   定数: pi, e
    ///
    /// 使い方:
    ///   var expr = ControlExpression.Parse("k*(pos2 - pos1) + d*(vel2 - vel1)");
    ///   if (expr.IsValid) double torque = expr.Evaluate(name => variables[name]);
    /// </summary>
    public sealed class ControlExpression
    {
        // 解析済みの計算木。変数解決関数(resolver)を受け取り、評価結果を返す。
        private readonly Func<Func<string, double>, double>? _root;

        public string Source { get; }
        public string? Error { get; }
        public bool IsValid => Error == null && _root != null;

        // この式が参照している変数名の一覧(検証・表示用)
        public IReadOnlyCollection<string> ReferencedVariables { get; }

        private ControlExpression(string source, Func<Func<string, double>, double>? root,
                                  string? error, HashSet<string> vars)
        {
            Source = source;
            _root = root;
            Error = error;
            ReferencedVariables = vars;
        }

        /// <summary>式文字列を解析する。失敗しても例外は投げず、Error にメッセージを入れて返す。</summary>
        public static ControlExpression Parse(string? source)
        {
            source ??= string.Empty;
            var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(source))
                return new ControlExpression(source, null, "式が空です", vars);

            try
            {
                var parser = new Parser(source, vars);
                var root = parser.ParseAll();
                return new ControlExpression(source, root, null, vars);
            }
            catch (FormatException ex)
            {
                return new ControlExpression(source, null, ex.Message, vars);
            }
        }

        /// <summary>解析済みの式を評価する。resolver は変数名を受け取って値を返す。</summary>
        public double Evaluate(Func<string, double> resolver)
        {
            if (_root == null) throw new InvalidOperationException("無効な式は評価できません: " + Error);
            return _root(resolver);
        }

        /// <summary>評価を試みる。無効な式・未定義変数・0除算などが起きたら false を返す。</summary>
        public bool TryEvaluate(Func<string, double> resolver, out double value)
        {
            value = 0;
            if (_root == null) return false;
            try
            {
                double v = _root(resolver);
                if (double.IsNaN(v) || double.IsInfinity(v)) return false;
                value = v;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ===== 再帰下降パーサ =====
        // 文法:
        //   expr   := term (('+'|'-') term)*
        //   term   := power (('*'|'/'|'%') power)*
        //   power  := unary ('^' power)?          // 右結合
        //   unary  := ('+'|'-') unary | primary
        //   primary:= number | ident ('(' args ')')? | '(' expr ')'
        //   args   := expr (',' expr)*
        // 関数として扱う名前の一覧。これ以外の識別子は変数として扱う。
        // (例: k(x) は関数ではないので「k × (x)」の暗黙の掛け算になる)
        private static readonly HashSet<string> KnownFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "abs", "sign", "sqrt", "sin", "cos", "tan", "exp", "log",
            "deg", "rad", "min", "max", "clamp"
        };

        private sealed class Parser
        {
            private readonly string _s;
            private readonly HashSet<string> _vars;
            private int _pos;

            public Parser(string s, HashSet<string> vars)
            {
                _s = s;
                _vars = vars;
                _pos = 0;
            }

            public Func<Func<string, double>, double> ParseAll()
            {
                var node = ParseExpr();
                SkipWhitespace();
                if (_pos != _s.Length)
                    throw new FormatException($"式の {_pos + 1} 文字目付近を解釈できません: '{_s[_pos]}'");
                return node;
            }

            private void SkipWhitespace()
            {
                while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++;
            }

            private char? Peek()
            {
                SkipWhitespace();
                return _pos < _s.Length ? _s[_pos] : (char?)null;
            }

            private Func<Func<string, double>, double> ParseExpr()
            {
                var left = ParseTerm();
                while (true)
                {
                    char? c = Peek();
                    if (c == '+') { _pos++; var r = ParseTerm(); var l = left; left = v => l(v) + r(v); }
                    else if (c == '-') { _pos++; var r = ParseTerm(); var l = left; left = v => l(v) - r(v); }
                    else break;
                }
                return left;
            }

            private Func<Func<string, double>, double> ParseTerm()
            {
                var left = ParsePower();
                while (true)
                {
                    char? c = Peek();
                    if (c == '*') { _pos++; var r = ParsePower(); var l = left; left = v => l(v) * r(v); }
                    else if (c == '/') { _pos++; var r = ParsePower(); var l = left; left = v => l(v) / r(v); }
                    else if (c == '%') { _pos++; var r = ParsePower(); var l = left; left = v => l(v) % r(v); }
                    // 暗黙の掛け算: 演算子なしで次の因子が始まったら掛け算とみなす。
                    // 例) k(x) → k*(x)、2pos1 → 2*pos1、d(vel1) → d*(vel1)
                    else if (c.HasValue && (char.IsLetter(c.Value) || char.IsDigit(c.Value)
                                            || c.Value == '_' || c.Value == '.' || c.Value == '('))
                    {
                        var r = ParsePower(); var l = left; left = v => l(v) * r(v);
                    }
                    else break;
                }
                return left;
            }

            private Func<Func<string, double>, double> ParsePower()
            {
                var baseNode = ParseUnary();
                char? c = Peek();
                if (c == '^')
                {
                    _pos++;
                    var exp = ParsePower(); // 右結合
                    var b = baseNode;
                    return v => Math.Pow(b(v), exp(v));
                }
                return baseNode;
            }

            private Func<Func<string, double>, double> ParseUnary()
            {
                char? c = Peek();
                if (c == '+') { _pos++; return ParseUnary(); }
                if (c == '-') { _pos++; var n = ParseUnary(); return v => -n(v); }
                return ParsePrimary();
            }

            private Func<Func<string, double>, double> ParsePrimary()
            {
                char? c = Peek();
                if (c == null) throw new FormatException("式が途中で終わっています");

                if (c == '(')
                {
                    _pos++;
                    var node = ParseExpr();
                    if (Peek() != ')') throw new FormatException("閉じ括弧 ')' が足りません");
                    _pos++;
                    return node;
                }

                if (char.IsDigit(c.Value) || c.Value == '.')
                    return ParseNumber();

                if (char.IsLetter(c.Value) || c.Value == '_')
                    return ParseIdentifier();

                throw new FormatException($"予期しない文字です: '{c.Value}'");
            }

            private Func<Func<string, double>, double> ParseNumber()
            {
                SkipWhitespace();
                int start = _pos;
                while (_pos < _s.Length &&
                       (char.IsDigit(_s[_pos]) || _s[_pos] == '.' ||
                        _s[_pos] == 'e' || _s[_pos] == 'E' ||
                        ((_s[_pos] == '+' || _s[_pos] == '-') && _pos > start &&
                         (_s[_pos - 1] == 'e' || _s[_pos - 1] == 'E'))))
                {
                    _pos++;
                }
                string token = _s.Substring(start, _pos - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                    throw new FormatException($"数値として解釈できません: '{token}'");
                return _ => val;
            }

            private Func<Func<string, double>, double> ParseIdentifier()
            {
                SkipWhitespace();
                int start = _pos;
                while (_pos < _s.Length && (char.IsLetterOrDigit(_s[_pos]) || _s[_pos] == '_'))
                    _pos++;
                string name = _s.Substring(start, _pos - start);

                // 関数呼び出し? (既知の関数名のときだけ。k(x) のような変数は掛け算として扱う)
                if (Peek() == '(' && KnownFunctions.Contains(name))
                {
                    _pos++;
                    var args = new List<Func<Func<string, double>, double>>();
                    if (Peek() != ')')
                    {
                        args.Add(ParseExpr());
                        while (Peek() == ',') { _pos++; args.Add(ParseExpr()); }
                    }
                    if (Peek() != ')') throw new FormatException($"関数 '{name}' の閉じ括弧がありません");
                    _pos++;
                    return BuildFunction(name, args);
                }

                // 定数?
                string lower = name.ToLowerInvariant();
                if (lower == "pi") return _ => Math.PI;
                if (lower == "e") return _ => Math.E;

                // それ以外は変数
                _vars.Add(name);
                return resolver => resolver(name);
            }

            private static Func<Func<string, double>, double> BuildFunction(
                string name, List<Func<Func<string, double>, double>> args)
            {
                void Need(int n)
                {
                    if (args.Count != n)
                        throw new FormatException($"関数 '{name}' は引数 {n} 個が必要です (実際: {args.Count})");
                }

                switch (name.ToLowerInvariant())
                {
                    case "abs": Need(1); return v => Math.Abs(args[0](v));
                    case "sign": Need(1); return v => Math.Sign(args[0](v));
                    case "sqrt": Need(1); return v => Math.Sqrt(args[0](v));
                    case "sin": Need(1); return v => Math.Sin(args[0](v));
                    case "cos": Need(1); return v => Math.Cos(args[0](v));
                    case "tan": Need(1); return v => Math.Tan(args[0](v));
                    case "exp": Need(1); return v => Math.Exp(args[0](v));
                    case "log": Need(1); return v => Math.Log(args[0](v));
                    case "deg": Need(1); return v => args[0](v) * 180.0 / Math.PI;
                    case "rad": Need(1); return v => args[0](v) * Math.PI / 180.0;
                    case "min": Need(2); return v => Math.Min(args[0](v), args[1](v));
                    case "max": Need(2); return v => Math.Max(args[0](v), args[1](v));
                    case "clamp":
                        Need(3);
                        return v => Math.Max(args[1](v), Math.Min(args[2](v), args[0](v)));
                    default:
                        throw new FormatException($"未知の関数です: '{name}'");
                }
            }
        }
    }
}
