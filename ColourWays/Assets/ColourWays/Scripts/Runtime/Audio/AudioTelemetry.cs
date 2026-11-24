using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
#if COLOURWAYS_FMOD
using FMOD;
using FMODUnity;
#endif

namespace ColourWays.Audio
{
    public struct AudioEventRecord
    {
        public float TimeSinceStartup;
        public string SceneName;
        public string EventName;
        public string Mode;
        public string Result;
        public int TriggerCount;
        public Vector3 SourcePosition;
        public Vector3 ListenerPosition;
        public float Distance;
        public float MinDistance;
        public float MaxDistance;
        public string Parameters;
    }

    public struct FmodPerfSnapshot
    {
        public bool IsFmodActive;
        public int LoadedBankCount;
        public float CpuDsp;
        public float CpuStream;
        public float CpuGeometry;
        public float CpuUpdate;
        public float CpuTotal;
        public int MemoryCurrentBytes;
        public int MemoryMaxBytes;
        public int ChannelsPlaying;
        public int RealChannelsPlaying;
        public Vector3 ListenerPosition;
        public bool HasStudioListener;
    }

    /// <summary>
    /// Tracks triggered audio events, trigger counts, 3D listener distances, FMOD CPU/memory stats,
    /// and writes a persistent CSV log suitable for build validation and technical commentary.
    /// </summary>
    public sealed class AudioTelemetry : IDisposable
    {
        private const int MaxRecentRecords = 64;
        private readonly List<AudioEventRecord> _recent = new List<AudioEventRecord>(MaxRecentRecords);
        private readonly Dictionary<string, int> _triggerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private StreamWriter _csvWriter;
        private bool _logToConsole;

        public IReadOnlyList<AudioEventRecord> RecentRecords => _recent;
        public IReadOnlyDictionary<string, int> TriggerCounts => _triggerCounts;
        public int TotalTriggers { get; private set; }
        public string CsvFilePath { get; private set; }

        public AudioTelemetry(bool logToConsole, bool enableCsv)
        {
            _logToConsole = logToConsole;
            if (enableCsv)
                OpenCsv();
        }

        private void OpenCsv()
        {
            try
            {
                CsvFilePath = Path.Combine(Application.persistentDataPath, "fmod_audio_log.csv");
                bool exists = File.Exists(CsvFilePath);
                _csvWriter = new StreamWriter(CsvFilePath, append: true, encoding: Encoding.UTF8) { AutoFlush = true };
                if (!exists)
                {
                    _csvWriter.WriteLine(
                        "UtcTimestamp,SessionTimeSec,Scene,Event,Mode,Result,TriggerCount," +
                        "SourceX,SourceY,SourceZ,ListenerX,ListenerY,ListenerZ,Distance,MinRadius,MaxRadius," +
                        "Parameters,FmodCpuTotalPct,FmodMemoryKB,ActiveChannels");
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[ColourWays.Audio] Could not open CSV telemetry log: {ex.Message}");
                _csvWriter = null;
            }
        }

        public void RecordEvent(
            string eventName,
            string mode,
            string result,
            Vector3 sourcePosition,
            Vector3 listenerPosition,
            float minDistance,
            float maxDistance,
            string parameters,
            in FmodPerfSnapshot perf)
        {
            TotalTriggers++;
            _triggerCounts.TryGetValue(eventName, out int count);
            count++;
            _triggerCounts[eventName] = count;

            float distance = Vector3.Distance(sourcePosition, listenerPosition);
            string scene = SceneManager.GetActiveScene().name;

            var record = new AudioEventRecord
            {
                TimeSinceStartup = Time.realtimeSinceStartup,
                SceneName = scene,
                EventName = eventName,
                Mode = mode,
                Result = result,
                TriggerCount = count,
                SourcePosition = sourcePosition,
                ListenerPosition = listenerPosition,
                Distance = distance,
                MinDistance = minDistance,
                MaxDistance = maxDistance,
                Parameters = parameters ?? string.Empty,
            };

            if (_recent.Count >= MaxRecentRecords)
                _recent.RemoveAt(0);
            _recent.Add(record);

            if (_logToConsole)
            {
                string paramPart = string.IsNullOrEmpty(record.Parameters) ? "" : $" | params=[{record.Parameters}]";
                string radiusPart = maxDistance > 0f ? $" | radius={minDistance:F1}-{maxDistance:F1}m" : "";
                UnityEngine.Debug.Log(
                    $"[ColourWays.Audio] [{mode}] {eventName} (#{count}) -> {result} | " +
                    $"dist={distance:F2}m{radiusPart}{paramPart} | scene={scene}");
            }

            if (_csvWriter != null)
            {
                var inv = CultureInfo.InvariantCulture;
                string safeParams = (record.Parameters ?? "").Replace(",", ";");
                _csvWriter.WriteLine(string.Format(
                    inv,
                    "{0:O},{1:F3},{2},{3},{4},{5},{6},{7:F2},{8:F2},{9:F2},{10:F2},{11:F2},{12:F2},{13:F2},{14:F2},{15:F2},{16},{17:F3},{18:F1},{19}",
                    DateTime.UtcNow,
                    record.TimeSinceStartup,
                    scene,
                    eventName,
                    mode,
                    result,
                    count,
                    sourcePosition.x, sourcePosition.y, sourcePosition.z,
                    listenerPosition.x, listenerPosition.y, listenerPosition.z,
                    distance,
                    minDistance,
                    maxDistance,
                    safeParams,
                    perf.CpuTotal,
                    perf.MemoryCurrentBytes / 1024f,
                    perf.ChannelsPlaying));
            }
        }

        public static FmodPerfSnapshot SamplePerformance(Vector3 fallbackListenerPos)
        {
            var snap = new FmodPerfSnapshot
            {
                ListenerPosition = fallbackListenerPos,
            };

#if COLOURWAYS_FMOD
            snap.IsFmodActive = true;
            try
            {
                if (RuntimeManager.IsInitialized)
                {
                    if (RuntimeManager.StudioSystem.getBankCount(out int bankCount) == RESULT.OK)
                        snap.LoadedBankCount = bankCount;

                    if (RuntimeManager.CoreSystem.getCPUUsage(out CPU_USAGE cpu) == RESULT.OK)
                    {
                        snap.CpuDsp = cpu.dsp;
                        snap.CpuStream = cpu.stream;
                        snap.CpuGeometry = cpu.geometry;
                        snap.CpuUpdate = cpu.update;
                        snap.CpuTotal = cpu.dsp + cpu.stream + cpu.geometry + cpu.update;
                    }

                    if (Memory.GetStats(out int currentAlloc, out int maxAlloc) == RESULT.OK)
                    {
                        snap.MemoryCurrentBytes = currentAlloc;
                        snap.MemoryMaxBytes = maxAlloc;
                    }

                    if (RuntimeManager.CoreSystem.getChannelsPlaying(out int channels, out int realChannels) == RESULT.OK)
                    {
                        snap.ChannelsPlaying = channels;
                        snap.RealChannelsPlaying = realChannels;
                    }

                    if (RuntimeManager.StudioSystem.getListenerAttributes(0, out ATTRIBUTES_3D attrs) == RESULT.OK)
                    {
                        snap.ListenerPosition = new Vector3(attrs.position.x, attrs.position.y, attrs.position.z);
                        snap.HasStudioListener = UnityEngine.Object.FindAnyObjectByType<StudioListener>() != null;
                    }
                }
            }
            catch (Exception)
            {
                // Keep snapshot resilient if FMOD is shutting down or uninitialised.
            }
#endif
            return snap;
        }

        public void LogSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[ColourWays.Audio] === Session Audio Summary (Total Triggers: {TotalTriggers}) ===");
            foreach (var kv in _triggerCounts)
                sb.AppendLine($"  • {kv.Key}: {kv.Value}");
            if (!string.IsNullOrEmpty(CsvFilePath))
                sb.AppendLine($"  • CSV Log: {CsvFilePath}");
            UnityEngine.Debug.Log(sb.ToString());
        }

        public void Dispose()
        {
            if (_csvWriter != null)
            {
                _csvWriter.Flush();
                _csvWriter.Dispose();
                _csvWriter = null;
            }
        }
    }
}
