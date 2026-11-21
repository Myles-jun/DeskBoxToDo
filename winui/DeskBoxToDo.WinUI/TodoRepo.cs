using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeskBoxToDo.WinUI
{
    /// <summary>Read/write the shared deskbox-style todo store, preserving unknown fields.</summary>
    public static class TodoRepo
    {
        public static string Path => TodoStore.CurrentPath();

        public static JsonObject ReadRoot()
        {
            try
            {
                if (File.Exists(Path))
                {
                    JsonNode node = JsonNode.Parse(File.ReadAllText(Path));
                    if (node is JsonObject obj)
                    {
                        if (obj["items"] is not JsonArray)
                        {
                            obj["items"] = new JsonArray();
                        }

                        obj["version"] ??= 3;
                        return obj;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("repo read failed: " + ex.Message);
            }

            return new JsonObject { ["version"] = 3, ["items"] = new JsonArray() };
        }

        public static void WriteRoot(JsonObject root)
        {
            try
            {
                string path = Path;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                string temp = path + ".v2tmp";
                File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
            catch (Exception ex)
            {
                Console.WriteLine("repo write failed: " + ex.Message);
            }
        }

        public static JsonObject Find(JsonObject root, string id)
        {
            if (root["items"] is not JsonArray items)
            {
                return null;
            }

            foreach (JsonNode node in items)
            {
                if (node is JsonObject obj && (string)obj["id"] == id)
                {
                    return obj;
                }
            }

            return null;
        }

        public static void SetCompleted(string id, bool completed)
        {
            JsonObject root = ReadRoot();
            JsonObject item = Find(root, id);
            if (item == null)
            {
                return;
            }

            item["isCompleted"] = completed;
            item["completedAt"] = completed ? DateTimeOffset.UtcNow.ToString("o") : null;
            WriteRoot(root);
        }

        public static void SetImportant(string id, bool important)
        {
            JsonObject root = ReadRoot();
            JsonObject item = Find(root, id);
            if (item == null)
            {
                return;
            }

            item["isImportant"] = important;
            WriteRoot(root);
        }

        public static void Delete(string id)
        {
            JsonObject root = ReadRoot();
            if (root["items"] is not JsonArray items)
            {
                return;
            }

            for (int index = items.Count - 1; index >= 0; index--)
            {
                if (items[index] is JsonObject obj && (string)obj["id"] == id)
                {
                    items.RemoveAt(index);
                }
            }

            WriteRoot(root);
        }

        /// <summary>Creates or updates one item and returns its id.</summary>
        public static string Save(
            string id,
            string text,
            DateTimeOffset? due,
            string recurrence,
            string color,
            bool important,
            string notes,
            IEnumerable<string> steps)
        {
            JsonObject root = ReadRoot();
            JsonObject item = string.IsNullOrEmpty(id) ? null : Find(root, id);
            bool isNew = item == null;

            if (isNew)
            {
                item = new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString("N"),
                    ["text"] = text,
                    ["isCompleted"] = false,
                    ["isImportant"] = important,
                    ["colorMarker"] = color,
                    ["dueDate"] = null,
                    ["recurrence"] = null,
                    ["steps"] = new JsonArray(),
                    ["notes"] = notes ?? "",
                    ["attachments"] = new JsonArray(),
                    ["sortOrder"] = ((JsonArray)root["items"]).Count
                };

                ((JsonArray)root["items"]).Add(item);
            }
            else
            {
                item["text"] = text;
                item["isImportant"] = important;
                item["colorMarker"] = color;
                item["notes"] = notes ?? "";
            }

            item["dueDate"] = due?.ToString("o");

            if (string.IsNullOrEmpty(recurrence) || recurrence == "none")
            {
                item["recurrence"] = null;
            }
            else
            {
                item["recurrence"] = new JsonObject
                {
                    ["mode"] = recurrence,
                    ["anchorDueDate"] = due?.ToString("o")
                };
            }

            var stepArray = new JsonArray();
            int order = 0;
            foreach (string step in steps ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(step))
                {
                    continue;
                }

                stepArray.Add(new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString("N"),
                    ["text"] = step.Trim(),
                    ["isCompleted"] = false,
                    ["sortOrder"] = order++
                });
            }

            // Keep completion state of existing steps when their text matches.
            if (!isNew && item["steps"] is JsonArray oldSteps)
            {
                foreach (JsonNode node in stepArray)
                {
                    var fresh = node as JsonObject;
                    foreach (JsonNode oldNode in oldSteps)
                    {
                        if (oldNode is JsonObject old && (string)old["text"] == (string)fresh["text"])
                        {
                            fresh["isCompleted"] = (bool?)old["isCompleted"] ?? false;
                        }
                    }
                }
            }

            item["steps"] = stepArray;
            WriteRoot(root);
            return (string)item["id"];
        }

        public static List<string> StepTexts(JsonObject item)
        {
            var list = new List<string>();
            if (item?["steps"] is JsonArray steps)
            {
                foreach (JsonNode node in steps)
                {
                    if (node is JsonObject step && step["text"] != null)
                    {
                        list.Add((string)step["text"]);
                    }
                }
            }

            return list;
        }
    }
}
