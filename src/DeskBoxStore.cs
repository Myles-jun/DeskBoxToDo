using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskBoxToDo
{
    internal sealed class ImportResult
    {
        public string ItemId = "";
        public string StorePath = "";
        public string WidgetId = "";
        public int SortOrder;
        public bool DeskBoxWasRunning;
        public bool DeskBoxRestarted;
    }

    /// <summary>
    /// Reads and writes DeskBox's own JSON stores. DeskBox keeps each Todo
    /// widget's items in data/widgets/&lt;id&gt;/todo.json; the widget view model
    /// is the only live writer, so the import always stops DeskBox first and
    /// lets it reload the file on the next start.
    /// </summary>
    internal static class DeskBoxStore
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static string ExpandDataRoot(string configured)
        {
            string expanded = Environment.ExpandEnvironmentVariables(configured ?? "");
            if (string.IsNullOrWhiteSpace(expanded))
            {
                expanded = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeskBox");
            }

            return Path.GetFullPath(expanded);
        }

        public static string ResolveDataRoot(AppConfig config)
        {
            if (Directory.Exists(Path.Combine(ExpandDataRoot(config.DeskBoxDataRoot), "data")))
            {
                return ExpandDataRoot(config.DeskBoxDataRoot);
            }

            // Fall back to the production root when the configured one has no data yet.
            string production = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeskBox");
            return production;
        }

        public static string FindTodoWidgetId(string dataRoot)
        {
            string layoutPath = Path.Combine(dataRoot, "data", "widget-layout.json");
            if (File.Exists(layoutPath))
            {
                try
                {
                    var root = Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(layoutPath, Encoding.UTF8));
                    Dictionary<string, object> layout = GetMap(root, "layout");
                    if (layout != null)
                    {
                        foreach (object entry in GetList(layout, "widgets"))
                        {
                            var widget = entry as Dictionary<string, object>;
                            if (widget == null)
                            {
                                continue;
                            }

                            if (GetString(widget, "widgetKind") != "Todo")
                            {
                                continue;
                            }

                            if (GetBool(widget, "isDisabled"))
                            {
                                continue;
                            }

                            string id = GetString(widget, "id");
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                return id;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("widget-layout parse failed: " + ex.Message);
                }
            }

            // Fallback: any widget directory that already owns a todo store.
            string widgetsRoot = Path.Combine(dataRoot, "data", "widgets");
            if (Directory.Exists(widgetsRoot))
            {
                string best = null;
                DateTime bestWrite = DateTime.MinValue;
                foreach (string dir in Directory.EnumerateDirectories(widgetsRoot))
                {
                    string store = Path.Combine(dir, "todo.json");
                    if (!File.Exists(store))
                    {
                        continue;
                    }

                    DateTime write = File.GetLastWriteTimeUtc(store);
                    if (best == null || write > bestWrite)
                    {
                        best = Path.GetFileName(dir);
                        bestWrite = write;
                    }
                }

                if (best != null)
                {
                    return best;
                }
            }

            return null;
        }

        public static bool InsertAtTop(string dataRoot)
        {
            try
            {
                string settingsPath = Path.Combine(dataRoot, "data", "settings.json");
                if (!File.Exists(settingsPath))
                {
                    return true;
                }

                var settings = Serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(settingsPath, Encoding.UTF8));
                string position = GetString(settings, "todoNewTaskPosition");
                if (string.Equals(position, "Bottom", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Write("settings read failed: " + ex.Message);
            }

            return true;
        }

        public static string ReadDeviceId(string dataRoot)
        {
            try
            {
                string path = Path.Combine(dataRoot, "data", "device.id");
                if (File.Exists(path))
                {
                    string id = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (id.Length > 0)
                    {
                        return id;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("device id read failed: " + ex.Message);
            }

            return Guid.NewGuid().ToString("N");
        }

        public static ImportResult Append(TodoDraft draft, AppConfig config, string dataRoot, string widgetId)
        {
            if (string.IsNullOrWhiteSpace(widgetId))
            {
                throw new InvalidOperationException("DeskBox 里还没有待办格子，请先在 DeskBox 中创建一个待办格子。");
            }

            string dir = Path.Combine(dataRoot, "data", "widgets", widgetId);
            string storePath = Path.Combine(dir, "todo.json");
            Directory.CreateDirectory(dir);

            Dictionary<string, object> root;
            if (File.Exists(storePath))
            {
                string existing = File.ReadAllText(storePath, Encoding.UTF8);
                root = string.IsNullOrWhiteSpace(existing)
                    ? NewRoot()
                    : Serializer.Deserialize<Dictionary<string, object>>(existing) ?? NewRoot();
            }
            else
            {
                root = NewRoot();
            }

            var items = new List<Dictionary<string, object>>();
            foreach (object entry in GetList(root, "items"))
            {
                var item = entry as Dictionary<string, object>;
                if (item != null)
                {
                    items.Add(item);
                }
            }

            int existingCount = items.Count;

            bool insertTop = InsertAtTop(dataRoot);
            string deviceId = ReadDeviceId(dataRoot);
            Dictionary<string, object> created = BuildItem(draft, 0, deviceId, config);

            if (insertTop)
            {
                items.Insert(0, created);
            }
            else
            {
                items.Add(created);
            }

            if (items.Count != existingCount + 1)
            {
                throw new InvalidOperationException("合并校验失败：原有待办数量未按预期保留，已中止写入。");
            }

            for (int index = 0; index < items.Count; index++)
            {
                items[index]["sortOrder"] = index;
            }

            int version = 3;
            object versionValue;
            if (root.TryGetValue("version", out versionValue))
            {
                try
                {
                    version = Math.Max(3, Convert.ToInt32(versionValue));
                }
                catch
                {
                    version = 3;
                }
            }

            root["version"] = version;
            root["items"] = items.Cast<object>().ToArray();

            if (File.Exists(storePath))
            {
                try
                {
                    File.Copy(storePath, storePath + ".bak", true);
                }
                catch (Exception ex)
                {
                    Log.Write("backup before import failed: " + ex.Message);
                }
            }

            string json = Serializer.Serialize(root);
            string tempPath = storePath + ".import.tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Copy(tempPath, storePath, true);
            File.Delete(tempPath);

            var result = new ImportResult
            {
                ItemId = (string)created["id"],
                StorePath = storePath,
                WidgetId = widgetId,
                SortOrder = insertTop ? 0 : items.Count - 1
            };

            Log.Write(string.Format(
                CultureInfo.InvariantCulture,
                "imported item={0} widget={1} top={2} steps={3} due={4}",
                result.ItemId,
                widgetId,
                insertTop,
                draft.Steps.Count,
                draft.Due.HasValue ? draft.Due.Value.ToString("o", CultureInfo.InvariantCulture) : "none"));
            return result;
        }

        private static Dictionary<string, object> NewRoot()
        {
            return new Dictionary<string, object>
            {
                { "version", 3 },
                { "items", new object[0] }
            };
        }

        private static Dictionary<string, object> BuildItem(
            TodoDraft draft,
            int sortOrder,
            string deviceId,
            AppConfig config)
        {
            string nowUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var steps = new List<object>();
            int stepOrder = 0;
            foreach (string step in draft.Steps)
            {
                steps.Add(new Dictionary<string, object>
                {
                    { "id", Guid.NewGuid().ToString("N") },
                    { "text", step },
                    { "isCompleted", false },
                    { "sortOrder", stepOrder }
                });
                stepOrder++;
            }

            object colorMarker = null;
            string chosenMarker = string.IsNullOrWhiteSpace(draft.ColorMarker)
                ? config.ColorMarker
                : draft.ColorMarker;
            if (!string.IsNullOrWhiteSpace(chosenMarker))
            {
                string[] allowed = { "red", "orange", "yellow", "green", "blue", "purple", "teal", "pink" };
                string normalized = chosenMarker.Trim().ToLowerInvariant();
                if (allowed.Contains(normalized))
                {
                    colorMarker = normalized;
                }
            }

            return new Dictionary<string, object>
            {
                { "id", Guid.NewGuid().ToString("N") },
                { "text", draft.Title },
                { "isCompleted", false },
                { "isImportant", draft.Important },
                { "colorMarker", colorMarker },
                { "dueDate", draft.Due.HasValue ? draft.Due.Value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture) : null },
                { "recurrence", null },
                { "steps", steps.ToArray() },
                { "notes", string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim() },
                { "attachments", new object[0] },
                { "completedAt", null },
                { "reminderLastNotifiedAt", null },
                { "reminderDismissedForDueDate", null },
                { "reminderOffsetMinutes", null },
                { "snoozedUntil", null },
                { "snoozeLastNotifiedAt", null },
                { "recurrenceSeriesId", null },
                { "generatedNextItemId", null },
                { "sortOrder", sortOrder },
                { "createdAt", nowUtc },
                { "updatedAt", nowUtc },
                { "deviceId", deviceId },
                { "isDeleted", false }
            };
        }

        private static Dictionary<string, object> GetMap(Dictionary<string, object> parent, string key)
        {
            object value;
            if (parent != null && parent.TryGetValue(key, out value))
            {
                return value as Dictionary<string, object>;
            }

            return null;
        }

        private static List<object> GetList(Dictionary<string, object> parent, string key)
        {
            var list = new List<object>();
            object value;
            if (parent == null || !parent.TryGetValue(key, out value) || value == null)
            {
                return list;
            }

            // JavaScriptSerializer hands back ArrayList for JSON arrays.
            var array = value as object[];
            if (array != null)
            {
                list.AddRange(array);
                return list;
            }

            var arrayList = value as System.Collections.ArrayList;
            if (arrayList != null)
            {
                foreach (object entry in arrayList)
                {
                    list.Add(entry);
                }
            }

            return list;
        }

        private static string GetString(Dictionary<string, object> parent, string key)
        {
            object value;
            if (parent != null && parent.TryGetValue(key, out value) && value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static bool GetBool(Dictionary<string, object> parent, string key)
        {
            object value;
            if (parent != null && parent.TryGetValue(key, out value) && value is bool flag)
            {
                return flag;
            }

            return false;
        }
    }

    internal static class DeskBoxApp
    {
        public static List<Process> FindRunning(string exePath)
        {
            var found = new List<Process>();
            string full = SafeFullPath(exePath);
            foreach (Process process in Process.GetProcessesByName("DeskBox"))
            {
                try
                {
                    string path = SafeFullPath(process.MainModule.FileName);
                    if (full != null && string.Equals(path, full, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(process);
                        continue;
                    }
                }
                catch
                {
                    // Access denied or process gone: ignore.
                }

                process.Dispose();
            }

            return found;
        }

        public static bool Stop(string exePath, TimeSpan timeout, out int stoppedCount, out string error)
        {
            stoppedCount = 0;
            error = null;
            List<Process> running = FindRunning(exePath);
            if (running.Count == 0)
            {
                return true;
            }

            foreach (Process process in running)
            {
                try
                {
                    process.Kill();
                    stoppedCount++;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log.Write("kill failed: " + ex.Message);
                }
                finally
                {
                    process.Dispose();
                }
            }

            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (FindRunning(exePath).Count == 0)
                {
                    return true;
                }

                System.Threading.Thread.Sleep(80);
            }

            error = error ?? "DeskBox 没有在超时时间内退出。";
            return false;
        }

        public static bool Start(string exePath, out string error)
        {
            error = null;
            try
            {
                string directory = Path.GetDirectoryName(exePath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = string.IsNullOrEmpty(directory) ? Environment.CurrentDirectory : directory,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write("start failed: " + ex.Message);
                return false;
            }
        }

        private static string SafeFullPath(string path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }
    }

    /// <summary>Stop DeskBox, write the store, start DeskBox again.</summary>
    internal static class Importer
    {
        public static ImportResult Commit(TodoDraft draft, AppConfig config, bool restart, out string message)
        {
            message = "";
            string dataRoot = DeskBoxStore.ResolveDataRoot(config);
            string widgetId = DeskBoxStore.FindTodoWidgetId(dataRoot);
            bool wasRunning = DeskBoxApp.FindRunning(config.DeskBoxExePath).Count > 0;
            int stopped = 0;

            if (restart && config.RestartAfterWrite && wasRunning)
            {
                string error;
                if (!DeskBoxApp.Stop(config.DeskBoxExePath, TimeSpan.FromSeconds(8), out stopped, out error))
                {
                    message = "没能让 DeskBox 退出，为避免数据被覆盖，这次没有写入：" + error;
                    throw new InvalidOperationException(message);
                }
            }

            ImportResult result = DeskBoxStore.Append(draft, config, dataRoot, widgetId);
            result.DeskBoxWasRunning = wasRunning;

            if (restart && config.RestartAfterWrite && wasRunning)
            {
                string error;
                if (DeskBoxApp.Start(config.DeskBoxExePath, out error))
                {
                    result.DeskBoxRestarted = true;
                }
                else
                {
                    message = "待办已写入，但 DeskBox 没能重新启动：" + error;
                }
            }
            else if (!wasRunning)
            {
                message = "待办已写入；DeskBox 当前没在运行，下次启动就能看到。";
            }
            else
            {
                message = "待办已写入；DeskBox 仍在运行，重启后可见。";
            }

            return result;
        }
    }
}
