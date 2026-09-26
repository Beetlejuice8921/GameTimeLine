using System.Collections.Generic;
using System.Text;

namespace TimeMapGameplay
{
    /// <summary>Minimal RFC 4180 CSV reader/writer (quotes, escaped quotes, separators and newlines inside quotes).</summary>
    public static class Csv
    {
        /// <summary>Parses CSV text. Auto-detects ';' as separator when the header has no ','. Skips empty lines.</summary>
        public static List<List<string>> Parse(string text, char separator = '\0')
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;
            if (text[0] == '﻿') text = text.Substring(1);

            if (separator == '\0')
            {
                int lineEnd = text.IndexOf('\n');
                string header = lineEnd < 0 ? text : text.Substring(0, lineEnd);
                separator = header.Contains(",") || !header.Contains(";") ? ',' : ';';
            }

            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                    continue;
                }

                if (c == '"' && field.Length == 0) quoted = true;
                else if (c == separator) { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n') { EndRow(); }
                else field.Append(c);
            }
            EndRow();
            return rows;

            void EndRow()
            {
                row.Add(field.ToString());
                field.Clear();
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                row = new List<string>();
            }
        }

        public static string Write(IEnumerable<IReadOnlyList<string>> rows, char separator = ',')
        {
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                for (int i = 0; i < row.Count; i++)
                {
                    if (i > 0) sb.Append(separator);
                    sb.Append(Escape(row[i] ?? "", separator));
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        static string Escape(string value, char separator)
        {
            bool needsQuotes = value.IndexOf(separator) >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
