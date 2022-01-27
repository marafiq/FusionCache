﻿using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;
using ZiggyCreatures.Caching.Fusion.Internals.Memory;

namespace ZiggyCreatures.Caching.Fusion.Internals
{
	internal static class FusionCacheInternalUtils
	{
		/// <summary>
		/// Checks if the entry is logically expired.
		/// </summary>
		/// <returns>A <see cref="bool"/> indicating the logical expiration status.</returns>
		public static bool IsLogicallyExpired(this IFusionCacheEntry? entry)
		{
			if (entry?.Metadata is null)
				return false;

			return entry.Metadata.IsLogicallyExpired();
		}

		public static readonly Type CacheItemPriorityType = typeof(CacheItemPriority);

		public static string GetCurrentMemberName([CallerMemberName] string name = "")
		{
			return name;
		}

		public static Exception GetSingleInnerExceptionOrSelf(this AggregateException exc)
		{
			// TODO: NOT SURE ABOUT THIS
			//if (exc is null)
			//	return null;
			return (exc.InnerException is object && exc.InnerExceptions?.Count <= 1)
				? exc.InnerException
				: exc
			;
		}

		public static string? ToLogString_Expiration(this DateTimeOffset dt)
		{
			var now = DateTimeOffset.UtcNow;
			var delta = dt - now;

			if (delta == TimeSpan.Zero)
				return "now";

			if (delta.TotalSeconds > -1 && delta.TotalSeconds < 1)
				return delta.TotalMilliseconds.ToString("0") + "ms";

			if (delta.TotalMinutes > -1 && delta.TotalMinutes < 1)
				return delta.TotalSeconds.ToString("0") + "s";

			if (delta.TotalHours > -1 && delta.TotalHours < 1)
				return delta.TotalMinutes.ToString("0") + "m";

			return dt.ToString("o");
		}

		public static string? ToLogString(this MemoryCacheEntryOptions? options)
		{
			if (options is null)
				return null;

			return $"MEO[CEXP={options.AbsoluteExpiration!.Value.ToLogString_Expiration()} PR={options.Priority.ToLogString()} S={options.Size?.ToString()}]";
		}

		public static string? ToLogString(this DistributedCacheEntryOptions? options)
		{
			if (options is null)
				return null;

			return $"DEO[CEXP={options.AbsoluteExpiration?.ToLogString_Expiration()}]";
		}

		public static string? ToLogString(this FusionCacheEntryOptions? options)
		{
			if (options is null)
				return null;

			return "FEO" + options.ToString();
		}

		public static string? ToLogString(this IFusionCacheEntry? entry)
		{
			if (entry is null)
				return null;

			return "FE" + entry.ToString();
		}

		public static string? ToLogString(this TimeSpan ts)
		{
			if (ts == TimeSpan.Zero)
				return "0";

			if (ts.TotalSeconds > -1 && ts.TotalSeconds < 1)
				return ts.TotalMilliseconds.ToString("0") + "ms";

			if (ts.TotalMinutes > -1 && ts.TotalMinutes < 1)
				return ts.TotalSeconds.ToString("0") + "s";

			if (ts.TotalHours > -1 && ts.TotalHours < 1)
				return ts.TotalMinutes.ToString("0") + "m";

			return ts.ToString();
		}

		public static string? ToLogString_Timeout(this TimeSpan ts)
		{
			if (ts == Timeout.InfiniteTimeSpan)
				return "/";

			return ts.ToLogString();
		}

		public static string ToLogString(this CacheItemPriority priority)
		{
			switch (priority)
			{
				case CacheItemPriority.Low:
					return "L";
				case CacheItemPriority.Normal:
					return "N";
				case CacheItemPriority.High:
					return "H";
				case CacheItemPriority.NeverRemove:
					return "NR";
			}

			// FALLBACK
			return Enum.GetName(CacheItemPriorityType, priority);
		}

		public static FusionCacheDistributedEntry<TValue> AsDistributedEntry<TValue>(this IFusionCacheEntry entry)
		{
			if (entry is FusionCacheDistributedEntry<TValue>)
				return (FusionCacheDistributedEntry<TValue>)entry;

			return new FusionCacheDistributedEntry<TValue>(entry.GetValue<TValue>(), entry.Metadata);
		}

		public static FusionCacheMemoryEntry AsMemoryEntry(this IFusionCacheEntry entry)
		{
			if (entry is FusionCacheMemoryEntry)
				return (FusionCacheMemoryEntry)entry;

			return new FusionCacheMemoryEntry(entry.GetValue<object>(), entry.Metadata);
		}

		public static void SafeExecute<TEventArgs>(this EventHandler<TEventArgs> ev, string? operationId, string? key, IFusionCache cache, Func<TEventArgs> eventArgsBuilder, string eventName, ILogger? logger, LogLevel logLevel, bool syncExecution)
		{
			var invocations = ev.GetInvocationList();

			// WE ONLY TEST IF THE LOG LEVEL IS ENABLED ONCE: IN THAT CASE WE'LL USE THE LOGGER, OTHERWISE WE SET IT TO null TO AVOID CHECKING IT EVERY TIME INSIDE THE LOOP
			if (logger is object && logger.IsEnabled(logLevel) == false)
				logger = null;

			var e = eventArgsBuilder();

			foreach (EventHandler<TEventArgs> invocation in invocations)
			{
				if (syncExecution)
				{
					try
					{
						invocation(cache, e);
					}
					catch (Exception exc)
					{
						logger?.Log(logLevel, exc, "FUSION (O={CacheOperationId} K={CacheKey}): an error occurred while handling an event handler for {EventName}", operationId, key, eventName);
					}
				}
				else
				{
					// TODO: MAYBE (PROBABLY) MOVE THE Task.Run OUTSIDE OF THE FOR LOOP
					Task.Run(() =>
					{
						try
						{
							invocation(cache, e);
						}
						catch (Exception exc)
						{
							logger?.Log(logLevel, exc, "FUSION (O={CacheOperationId} K={CacheKey}): an error occurred while handling an event handler for {EventName}", operationId, key, eventName);
						}
					});
				}
			}
		}

		public static string GetBackplaneChannelName(FusionCacheOptions options)
		{
			var prefix = options.BackplaneChannelPrefix;
			if (string.IsNullOrWhiteSpace(prefix))
				prefix = options.CacheName;

			return $"{prefix}.Notifications";
		}
	}
}
