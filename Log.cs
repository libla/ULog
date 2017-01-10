using System;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using UnityObject = UnityEngine.Object;
using UnityDebug = UnityEngine.Debug;

public static class Log
{
	public enum Level
	{
		Debug,
		Trace,
		Warning,
		Error,
	}

	public delegate void Callback(Level level, string text);

	public static Level? StackTrace = null;
	public static bool IsDebug = true;
	public static event Callback Logger;

	#region 内部变量
	private static bool initialized = false;
	private static object actions_mtx = new object();
	private static List<HandlerAction> actions = new List<HandlerAction>();
	private static List<HandlerAction> actions_tmp = new List<HandlerAction>();
	private static bool thread_init = false;
	private static int threadID = 0;
	private static readonly StringBuilder stringbuilder = new StringBuilder();
	#endregion

	public static void Initialize()
	{
		if (!initialized && Application.isPlaying)
		{
			initialized = true;
			GameObject go = new GameObject("Log");
			UnityObject.DontDestroyOnLoad(go);
			go.hideFlags |= HideFlags.HideInHierarchy;
			go.AddComponent<Updater>();
		}
	}

	public static void Exception(Exception e)
	{
		if (!initialized)
			throw new InvalidOperationException();
		if (thread_init && threadID == Thread.CurrentThread.ManagedThreadId)
		{
			ExecWrite(DateTime.Now, e, GetStackTrace(Level.Error, e));
		}
		else
		{
			HandlerAction action = HandlerAction.Acquire();
			action.datetime = DateTime.Now;
			action.exception = e;
			action.stacks = GetStackTrace(Level.Error, e);
			lock (actions_mtx)
			{
				actions.Add(action);
			}
		}
	}

	public static void Error(string str)
	{
		Write(Level.Error, str);
	}

	public static void Warning(string str)
	{
		Write(Level.Warning, str);
	}

	public static void Trace(string str)
	{
		Write(Level.Trace, str);
	}

	public static void Debug(string str)
	{
		if (IsDebug)
			Write(Level.Debug, str);
	}

	private static void Write(Level level, string text)
	{
		if (!initialized)
			throw new InvalidOperationException();
		if (thread_init && threadID == Thread.CurrentThread.ManagedThreadId)
		{
			ExecWrite(DateTime.Now, level, text, GetStackTrace(level, 2));
		}
		else
		{
			HandlerAction action = HandlerAction.Acquire();
			action.datetime = DateTime.Now;
			action.level = level;
			action.text = text;
			action.stacks = GetStackTrace(level, 2);
			lock (actions_mtx)
			{
				actions.Add(action);
			}
		}
	}

	#region 获取堆栈
	private static readonly StackFrame[] EmptyStackFrame = new StackFrame[0];

	private static StackFrame[] GetStackTrace(Level level, int stack)
	{
		return !StackTrace.HasValue || StackTrace.Value > level ? EmptyStackFrame : GetStackTrace(new StackTrace(stack, true));
	}

	private static StackFrame[] GetStackTrace(Level level, Exception e)
	{
		return !StackTrace.HasValue || StackTrace.Value > level ? EmptyStackFrame : GetStackTrace(new StackTrace(e, true));
	}

	private static StackFrame[] GetStackTrace(StackTrace stacks)
	{
		return stacks.FrameCount == 0 ? EmptyStackFrame : stacks.GetFrames();
	}
	#endregion

	#region 格式化时间和堆栈
	private static string GetLogFormat(string str, DateTime datetime, StackFrame[] stacks)
	{
		stringbuilder.Append('[');
		AddWidthInt(4, datetime.Year);
		stringbuilder.Append('-');
		AddWidthInt(2, datetime.Month);
		stringbuilder.Append('-');
		AddWidthInt(2, datetime.Day);
		stringbuilder.Append(' ');
		AddWidthInt(2, datetime.Hour);
		stringbuilder.Append(':');
		AddWidthInt(2, datetime.Minute);
		stringbuilder.Append(':');
		AddWidthInt(2, datetime.Second);
		stringbuilder.Append('.');
		AddWidthInt(3, datetime.Millisecond);
		stringbuilder.Append(']');
		stringbuilder.Append(str);
		if (stacks.Length > 0)
		{
			stringbuilder.Append("\nStackTrace:");
			if (stacks.Length > 6)
			{
				for (int i = 0, j = i + 3; i < j; ++i)
				{
					StackFrame frame = stacks[i];
					string filename = frame.GetFileName();
					if (filename == null)
						continue;
					stringbuilder.Append("\n\t");
					stringbuilder.Append(filename);
					stringbuilder.Append(":");
					stringbuilder.Append(frame.GetFileLineNumber());
				}
				stringbuilder.Append("\n\t...");
				for (int i = stacks.Length - 3, j = i + 3; i < j; ++i)
				{
					StackFrame frame = stacks[i];
					string filename = frame.GetFileName();
					if (filename == null)
						continue;
					stringbuilder.Append("\n\t");
					stringbuilder.Append(frame.GetFileName());
					stringbuilder.Append(":");
					stringbuilder.Append(frame.GetFileLineNumber());
				}
			}
			else
			{
				for (int i = 0, j = stacks.Length; i < j; ++i)
				{
					StackFrame frame = stacks[i];
					string filename = frame.GetFileName();
					if (filename == null)
						continue;
					stringbuilder.Append("\n\t");
					stringbuilder.Append(frame.GetFileName());
					stringbuilder.Append(":");
					stringbuilder.Append(frame.GetFileLineNumber());
				}
			}
		}
		string result = stringbuilder.ToString();
		stringbuilder.Length = 0;
		return result;
	}

	private static void AddWidthInt(int width, int value, char fill = '0')
	{
		int measure = 10;
		int digit = 1;
		while (value >= measure)
		{
			measure *= 10;
			++digit;
		}
		for (int i = digit; i < width; ++i)
			stringbuilder.Append(fill);
		stringbuilder.Append(value);
	}
	#endregion

	private static void ExecWrite(DateTime datetime, Level level, string text, StackFrame[] stacks)
	{
		if (Logger != null)
		{
			try
			{
				Logger(level, GetLogFormat(text, datetime, stacks));
			}
			catch
			{
			}
		}
		else
		{
			switch (level)
			{
			case Level.Debug:
			case Level.Trace:
				UnityDebug.Log(text);
				break;
			case Level.Warning:
				UnityDebug.LogWarning(text);
				break;
			case Level.Error:
				UnityDebug.LogError(text);
				break;
			}
		}
	}

	private static void ExecWrite(DateTime datetime, Exception exception, StackFrame[] stacks)
	{
		ExecWrite(datetime, Level.Error, exception.ToString(), stacks);
	}

	#region 多线程调用支持优化
	private static void ExecWrite(HandlerAction action)
	{
		if (action.exception != null)
			ExecWrite(action.datetime, action.exception, action.stacks);
		else
			ExecWrite(action.datetime, action.level, action.text, action.stacks);
		action.exception = null;
		action.text = null;
		action.stacks = null;
	}

	private class HandlerAction
	{
		public Level level;
		public Exception exception;
		public string text;
		public StackFrame[] stacks;
		public DateTime datetime;

		private static readonly Stack<HandlerAction> pool = new Stack<HandlerAction>();
		public static HandlerAction Acquire()
		{
			HandlerAction action = null;
			if (pool.Count > 0)
			{
				lock (pool)
				{
					if (pool.Count > 0)
						action = pool.Pop();
				}
			}
			return action ?? new HandlerAction();
		}

		public static void Release(List<HandlerAction> actions)
		{
			lock (pool)
			{
				for (int i = 0, j = actions.Count; i < j; ++i)
				{
					pool.Push(actions[i]);
				}
			}
			actions.Clear();
		}
	}
	#endregion

	#region 主循环调用
	private class Updater : MonoBehaviour
	{
		void Start()
		{
			threadID = Thread.CurrentThread.ManagedThreadId;
			thread_init = true;
		}

		void Update()
		{
			if (actions.Count == 0)
				return;
			lock (actions_mtx)
			{
				var tmp = actions_tmp;
				actions_tmp = actions;
				actions = tmp;
			}
			for (int i = 0, j = actions_tmp.Count; i < j; ++i)
			{
				try
				{
					ExecWrite(actions_tmp[i]);
				}
				catch (Exception)
				{
				}
			}
			HandlerAction.Release(actions_tmp);
		}
	}
	#endregion
}