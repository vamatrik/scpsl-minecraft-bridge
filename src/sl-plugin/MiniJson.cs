using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SlBridge
{
    /// <summary>
    /// Крошечный JSON-разборщик без зависимостей (у BCL игры нет Span, System.Text.Json не годится).
    /// Объекты → Dictionary&lt;string, object&gt;, массивы → List&lt;object&gt;, числа → double.
    /// </summary>
    public static class MiniJson
    {
        public static Dictionary<string, object> ParseObject(string s)
        {
            int i = 0;
            return Parse(s, ref i) as Dictionary<string, object>;
        }

        private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object Parse(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = ParseString(s, ref i);
                    Ws(s, ref i); if (s[i] != ':') throw new FormatException("':' expected"); i++;
                    d[k] = Parse(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("',' or '}' expected");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Parse(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("',' or ']' expected");
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eEaNIinfty".IndexOf(s[i]) >= 0) i++;
            double v;
            return double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v) ? v : 0.0;
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("string expected");
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }

        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>Удобные геттеры для разобранного сообщения.</summary>
    public static class Msg
    {
        public static string Str(this Dictionary<string, object> m, string k, string def = null)
            => m.TryGetValue(k, out var v) && v is string s ? s : def;
        public static double Num(this Dictionary<string, object> m, string k, double def = 0)
            => m.TryGetValue(k, out var v) && v is double d ? d : def;
        public static bool Bool(this Dictionary<string, object> m, string k, bool def = false)
            => m.TryGetValue(k, out var v) && v is bool b ? b : def;
        public static bool Has(this Dictionary<string, object> m, string k) => m.ContainsKey(k);
        public static List<object> Arr(this Dictionary<string, object> m, string k)
            => m.TryGetValue(k, out var v) && v is List<object> l ? l : new List<object>();
    }
}
