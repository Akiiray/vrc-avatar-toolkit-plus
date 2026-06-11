using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Text;

namespace Akiiray.VRCAvatarToolkitPlus.Editor
{
    /// <summary>
    /// ツール共通のテキストレポート作成用クラス。
    /// Debug.Logにも流したい場合は、Warning/Error/LogToConsole を使います。
    /// </summary>
    public sealed class ToolkitReportBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder(16 * 1024);

        public ToolkitReportBuilder()
        {
        }

        public ToolkitReportBuilder(string title)
        {
            Header(title);
        }

        public void Header(string title)
        {
            Line("# " + NullSafe(title));
            Line("Generated: " + DateTime.Now.ToString(ToolkitConstants.DateTimeFormat));
            Line("Unity: " + Application.unityVersion);
            Line("Project: " + Application.dataPath);
            Blank();
        }

        public void Section(string title)
        {
            Separator();
            Line("## " + NullSafe(title));
        }

        public void Subsection(string title)
        {
            Line("-- " + NullSafe(title) + " --");
        }

        public void KeyValue(string key, object value)
        {
            Line(NullSafe(key) + ": " + (value == null ? "<null>" : value.ToString()));
        }

        public void Line(string text = "")
        {
            _sb.AppendLine(text ?? string.Empty);
        }

        public void Blank()
        {
            _sb.AppendLine();
        }

        public void Separator()
        {
            _sb.AppendLine("============================================================");
        }

        public void Warning(string text, bool alsoConsole = true)
        {
            string line = "[Warning] " + NullSafe(text);
            _sb.AppendLine(line);
            if (alsoConsole) Debug.LogWarning(text);
        }

        public void Error(string text, bool alsoConsole = true)
        {
            string line = "[Error] " + NullSafe(text);
            _sb.AppendLine(line);
            if (alsoConsole) Debug.LogError(text);
        }

        public void Info(string text, bool alsoConsole = false)
        {
            string line = "[Info] " + NullSafe(text);
            _sb.AppendLine(line);
            if (alsoConsole) Debug.Log(text);
        }

        public void Append(string text)
        {
            _sb.Append(text ?? string.Empty);
        }

        public void AppendReport(ToolkitReportBuilder other)
        {
            if (other == null) return;
            _sb.Append(other.ToString());
        }

        public void LogToConsole()
        {
            Debug.Log(ToString());
        }

        public override string ToString()
        {
            return _sb.ToString();
        }

        private static string NullSafe(string text)
        {
            return string.IsNullOrEmpty(text) ? "<empty>" : text;
        }
    }

    /// <summary>
    /// コピーしやすい共通ログウィンドウ。
    /// 既存ツールの独自ログウィンドウは、最終的にこれへ寄せる想定です。
    /// </summary>
    public sealed class ToolkitLogWindow : EditorWindow
    {
        private static string _title = ToolkitConstants.ProductName + " - Log";
        private static string _text = string.Empty;

        private Vector2 _scroll;
        private GUIStyle _textAreaStyle;

        public static void Open(string title, string text)
        {
            _title = string.IsNullOrEmpty(title) ? ToolkitConstants.ProductName + " - Log" : title;
            _text = text ?? string.Empty;

            var window = GetWindow<ToolkitLogWindow>(_title);
            window.titleContent = new GUIContent(_title);
            window.minSize = new Vector2(720, 420);
            window.Show();
            window.Repaint();
        }

        public static void Open(string title, ToolkitReportBuilder report)
        {
            Open(title, report == null ? string.Empty : report.ToString());
        }

        private void OnGUI()
        {
            EnsureStyles();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("全コピー", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    EditorGUIUtility.systemCopyBuffer = _text ?? string.Empty;
                    Debug.Log("ログをクリップボードへコピーしました。");
                }

                if (GUILayout.Button("Consoleへ出力", EditorStyles.toolbarButton, GUILayout.Width(110)))
                {
                    Debug.Log(_text ?? string.Empty);
                }

                if (GUILayout.Button("txt保存", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    SaveTextFile();
                }

                if (GUILayout.Button("クリア", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    _text = string.Empty;
                    GUI.FocusControl(null);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Ctrl+A / Ctrl+C でもコピーできます", EditorStyles.miniLabel, GUILayout.Width(230));
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _text = EditorGUILayout.TextArea(_text ?? string.Empty, _textAreaStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void EnsureStyles()
        {
            if (_textAreaStyle != null) return;

            _textAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = false,
                richText = false,
                font = EditorStyles.standardFont
            };
        }

        private static void SaveTextFile()
        {
            string defaultName = "VRCAvatarToolkitPlusLog_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
            string path = EditorUtility.SaveFilePanel("ログを保存", Application.dataPath, defaultName, "txt");
            if (string.IsNullOrEmpty(path)) return;

            File.WriteAllText(path, _text ?? string.Empty, Encoding.UTF8);
            AssetDatabase.Refresh();
            Debug.Log("ログを保存しました: " + path);
        }
    }
}
