using System;
using System.Text;
using UnityEngine;

namespace Network_Game.Diagnostics
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>
    /// Project logging facade.
    /// Keeps category-based messages while standardizing output through Unity's log system.
    /// </summary>
    public static class NGLog
    {
        private const string DataflowPrefix = "Dataflow";

        public static void Debug(string category, string message, UnityEngine.Object context = null)
        {
            Write(LogLevel.Debug, category, message, context);
        }

        public static void Info(string category, string message, UnityEngine.Object context = null)
        {
            Write(LogLevel.Info, category, message, context);
        }

        public static void Warn(string category, string message, UnityEngine.Object context = null)
        {
            Write(LogLevel.Warning, category, message, context);
        }

        public static void Error(string category, string message, UnityEngine.Object context = null)
        {
            Write(LogLevel.Error, category, message, context);
        }

        public static void Log(string category, string message, UnityEngine.Object context = null)
        {
            Info(category, message, context);
        }

        public static void LogWarning(
            string category,
            string message,
            UnityEngine.Object context = null
        )
        {
            Warn(category, message, context);
        }

        public static void LogError(
            string category,
            string message,
            UnityEngine.Object context = null
        )
        {
            Error(category, message, context);
        }

        public static string Format(string message, params (string key, object value)[] data)
        {
            if (data == null || data.Length == 0)
            {
                return message ?? string.Empty;
            }

            var sb = new StringBuilder(message ?? string.Empty);
            sb.Append(" | ");

            for (int i = 0; i < data.Length; i++)
            {
                (string key, object value) pair = data[i];
                string effectiveKey = string.IsNullOrWhiteSpace(pair.key) ? $"arg{i}" : pair.key;
                sb.Append(effectiveKey);
                sb.Append('=');
                sb.Append(pair.value != null ? pair.value : "null");

                if (i < data.Length - 1)
                {
                    sb.Append(", ");
                }
            }

            return sb.ToString();
        }

        public static IDisposable BeginScope(
            string category,
            string operation,
            int requestId = 0,
            UnityEngine.Object context = null,
            LogLevel startLevel = LogLevel.Debug,
            LogLevel endLevel = LogLevel.Debug,
            params (string key, object value)[] data
        )
        {
            return new LogScope(
                category,
                operation,
                requestId,
                context,
                startLevel,
                endLevel,
                data
            );
        }

        public static void LogDataflowStep(
            string category,
            string flow,
            string step,
            LogLevel level = LogLevel.Info,
            int requestId = 0,
            UnityEngine.Object context = null,
            params (string key, object value)[] data
        )
        {
            string message = $"{DataflowPrefix}:{flow}:{step}";
            if (requestId != 0)
            {
                message = Format(message, ("requestId", requestId));
            }

            if (data != null && data.Length > 0)
            {
                message = Format(message, data);
            }

            Write(level, category, message, context);
        }

        private static void Write(
            LogLevel level,
            string category,
            string message,
            UnityEngine.Object context
        )
        {
            string prefix = string.IsNullOrWhiteSpace(category) ? string.Empty : $"[{category}] ";
            string fullMessage = $"{prefix}{message ?? string.Empty}";

            switch (level)
            {
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(fullMessage, context);
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(fullMessage, context);
                    break;
                default:
                    UnityEngine.Debug.Log(fullMessage, context);
                    break;
            }
        }

        private sealed class LogScope : IDisposable
        {
            private readonly string m_Category;
            private readonly string m_Operation;
            private readonly int m_RequestId;
            private readonly UnityEngine.Object m_Context;
            private readonly LogLevel m_EndLevel;
            private readonly float m_StartTime;
            private bool m_Disposed;

            public LogScope(
                string category,
                string operation,
                int requestId,
                UnityEngine.Object context,
                LogLevel startLevel,
                LogLevel endLevel,
                params (string key, object value)[] data
            )
            {
                m_Category = category;
                m_Operation = string.IsNullOrWhiteSpace(operation) ? "Scope" : operation;
                m_RequestId = requestId;
                m_Context = context;
                m_EndLevel = endLevel;
                m_StartTime = Time.realtimeSinceStartup;

                string beginMessage = $"{m_Operation} begin";
                if (requestId != 0)
                {
                    beginMessage = Format(beginMessage, ("requestId", requestId));
                }
                if (data != null && data.Length > 0)
                {
                    beginMessage = Format(beginMessage, data);
                }

                Write(startLevel, m_Category, beginMessage, m_Context);
            }

            public void Dispose()
            {
                if (m_Disposed)
                {
                    return;
                }
                m_Disposed = true;

                float elapsedMs = (Time.realtimeSinceStartup - m_StartTime) * 1000f;
                string endMessage = Format(
                    $"{m_Operation} end",
                    ("requestId", m_RequestId),
                    ("elapsedMs", Mathf.RoundToInt(elapsedMs))
                );
                Write(m_EndLevel, m_Category, endMessage, m_Context);
            }
        }
    }
}
