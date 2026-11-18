using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Non-interactive mode: "DeskBoxToDo.exe --text \"...\"" turns a sentence
    /// into a DeskBox todo without any UI. Used by tests and by future
    /// automation (chat bridges) that already know what the todo is.
    /// </summary>
    internal static class Cli
    {
        public static int Run(string[] args)
        {
            AttachConsoleForOutput();
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch
            {
                // No usable console (GUI launch): --out is the reliable channel.
            }

            string text = null;
            string title = null;
            string stepsArg = null;
            string dueArg = null;
            string importantArg = null;
            string outPath = null;
            string dataRoot = null;
            bool dryRun = false;
            bool noRestart = false;
            bool preview = false;
            bool noAi = false;
            string colorArg = null;

            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];
                switch (arg.ToLowerInvariant())
                {
                    case "--text":
                    case "--parse":
                        text = Next(args, ref index);
                        break;
                    case "--text-file":
                        string textFile = Next(args, ref index);
                        if (!string.IsNullOrWhiteSpace(textFile) && File.Exists(textFile))
                        {
                            text = File.ReadAllText(textFile, Encoding.UTF8);
                        }

                        break;
                    case "--title":
                        title = Next(args, ref index);
                        break;
                    case "--steps":
                        stepsArg = Next(args, ref index);
                        break;
                    case "--due":
                        dueArg = Next(args, ref index);
                        break;
                    case "--important":
                        importantArg = Next(args, ref index);
                        break;
                    case "--out":
                        outPath = Next(args, ref index);
                        break;
                    case "--data-root":
                        dataRoot = Next(args, ref index);
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--no-ai":
                        noAi = true;
                        break;
                    case "--color":
                        colorArg = Next(args, ref index);
                        break;
                    case "--no-restart":
                        noRestart = true;
                        break;
                    case "--preview":
                        preview = true;
                        break;
                    case "--help":
                    case "-h":
                        Console.WriteLine(Usage);
                        return 0;
                    case "--version":
                        Console.WriteLine("DeskBoxToDo 1.0");
                        return 0;
                    case "--writetest":
                        return WriteTest();
                    case "--settings":
                        return OpenSettings();
                    case "--ai-test":
                        return AiTest();
                    case "--menu-test":
                        return MenuTest();
                    case "--hotkey-test":
                        return HotkeyTest();
                    case "--async-demo":
                        return AsyncDemo();
                    case "--icon-test":
                        return IconTest();
                    case "--export-icon":
                        return ExportIcon(args);
                    case "--onboarding":
                        return OnboardingTest();
                    case "--todo":
                        return TodoTest();
                    case "--todo-selftest":
                        return TodoSelfTest();
                    case "--edit-test":
                        return EditTest();
                    case "--detail-test":
                        return DetailTest();
                    case "--time-test":
                        return TimeTest();
                    case "--date-test":
                        return DateTest();
                    case "--update-test":
                        return UpdateTest();
                    case "--picker-test":
                        return PickerTest();
                    case "--form-selftest":
                        return FormSelfTest();
                    default:
                        if (text == null && !arg.StartsWith("-", StringComparison.Ordinal))
                        {
                            text = arg;
                        }

                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine(Usage);
                return 2;
            }

            try
            {
                AppConfig config = AppConfig.Load();
                if (!string.IsNullOrWhiteSpace(dataRoot))
                {
                    config.DeskBoxDataRoot = dataRoot;
                }

                TodoDraft draft = TodoParser.Parse(text, config, DateTimeOffset.Now);

                if (!noAi && AiParser.IsConfigured(config) && (config.LlmAlways || !draft.Due.HasValue))
                {
                    TodoDraft ai = AiParser.Parse(text, config);
                    if (ai != null)
                    {
                        if (!string.IsNullOrWhiteSpace(ai.Title))
                        {
                            draft.Title = ai.Title;
                        }

                        if (ai.Steps.Count > 0)
                        {
                            draft.Steps = ai.Steps;
                        }

                        bool rulesExplicit = draft.Due.HasValue && draft.TimeFound;
                        if (ai.Due.HasValue && !rulesExplicit)
                        {
                            draft.Due = ai.Due;
                        }

                        draft.Important = ai.Important;

                        if (!string.IsNullOrEmpty(ai.RecurrenceMode) && ai.RecurrenceMode != TodoRecurrenceMode.None)
                        {
                            draft.RecurrenceMode = ai.RecurrenceMode;
                            if (!draft.Due.HasValue)
                            {
                                draft.Due = TodoService.DuePreset("today");
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    draft.Title = title.Trim();
                }

                if (!string.IsNullOrWhiteSpace(stepsArg))
                {
                    var steps = new List<string>();
                    foreach (string piece in stepsArg.Split(new[] { ';', '|', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = piece.Trim();
                        if (trimmed.Length > 0)
                        {
                            steps.Add(trimmed);
                        }
                    }

                    draft.Steps = steps;
                }

                if (!string.IsNullOrWhiteSpace(dueArg))
                {
                    DateTimeOffset parsedDue;
                    if (!DateTimeOffset.TryParse(
                            dueArg,
                            CultureInfo.CurrentCulture,
                            DateTimeStyles.AssumeLocal,
                            out parsedDue))
                    {
                        throw new ArgumentException("无法解析 --due 的取值：" + dueArg);
                    }

                    draft.Due = parsedDue;
                    draft.DateFound = true;
                }

                if (!string.IsNullOrWhiteSpace(colorArg))
                {
                    draft.ColorMarker = colorArg.Trim();
                }

                if (!string.IsNullOrWhiteSpace(importantArg))
                {
                    draft.Important = string.Equals(importantArg, "true", StringComparison.OrdinalIgnoreCase) ||
                                      importantArg == "1" ||
                                      string.Equals(importantArg, "yes", StringComparison.OrdinalIgnoreCase);
                }

                if (preview)
                {
                    Application.EnableVisualStyles();
                    using (var form = new PreviewForm(draft, config))
                    {
                        if (form.ShowDialog() != DialogResult.OK)
                        {
                            return 3;
                        }

                        draft = form.Result;
                    }
                }

                if (dryRun)
                {
                    Emit(Json(true, draft, null, "dry-run", ""), outPath);
                    return 0;
                }
                string message;
                bool deskBoxMode = TodoBackend.IsDeskBoxMode(config);
                var service = new TodoService(config);
                TodoItem added = service.Add(draft);
                string addedId = added == null ? null : added.Id;
                if (string.IsNullOrEmpty(addedId))
                {
                    throw new InvalidOperationException("写入失败，详见日志");
                }

                var result = new ImportResult
                {
                    ItemId = addedId,
                    StorePath = deskBoxMode ? "" : TodoBackend.StandalonePath,
                    WidgetId = deskBoxMode ? "" : "standalone",
                    DeskBoxWasRunning = deskBoxMode,
                    DeskBoxRestarted = deskBoxMode
                };
                message = deskBoxMode ? "已写进 DeskBox" : "已写入本机待办";
                Emit(Json(true, draft, result, message, ""), outPath);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("cli failed: " + ex);
                Emit(Json(false, null, null, ex.Message, ex.ToString()), outPath);
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>Exercises the preview dialog's control→draft mapping without showing it.</summary>
        private static int FormSelfTest()
        {
            AppConfig config = AppConfig.Load();
            TodoDraft draft = TodoParser.Parse(
                "第五章作业题：1，2（3）。周五上课交。",
                config,
                DateTimeOffset.Now);

            using (var form = new PreviewForm(draft, config))
            {
                bool applied = form.ApplyToDraft();
                TodoDraft result = form.Result;
                var payload = new Dictionary<string, object>
                {
                    { "applied", applied },
                    { "title", result.Title },
                    { "steps", result.Steps.ToArray() },
                    { "important", result.Important },
                    { "dueDate", result.Due.HasValue ? result.Due.Value.ToString("o", CultureInfo.InvariantCulture) : null },
                    { "dueText", TodoParser.Describe(result.Due) },
                { "recurrence", draft.RecurrenceMode ?? "" }
                };

                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                Console.WriteLine(PrettyJson.Format(serializer.Serialize(payload)));
            }

            return 0;
        }

        private static void Trace(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "trace.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Diagnostics only.
            }
        }

        private static int AsyncDemo()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            TodoDraft draft = TodoParser.Parse("周五交第五章作业，记得带上上机实验", config, DateTimeOffset.Now);
            using (var form = new PreviewForm(draft, config))
            {
                form.SetAiBusy("AI 解析中…");
                Task<TodoDraft> task = AiParser.ParseAsync("周五交第五章作业，记得带上上机实验", config);
                task.ContinueWith(completed =>
                {
                    try
                    {
                        TodoDraft ai = completed.Status == TaskStatus.RanToCompletion ? completed.Result : null;
                        form.BeginInvoke(new Action(() => form.ApplyAiResult(ai, false)));
                    }
                    catch
                    {
                        // Window already gone.
                    }
                });

                var closer = new System.Windows.Forms.Timer { Interval = 7000 };
                closer.Tick += (sender, args) =>
                {
                    closer.Stop();
                    form.DialogResult = DialogResult.Cancel;
                    form.Close();
                };
                closer.Start();
                form.ShowDialog();
            }

            return 0;
        }

        private static int HotkeyTest()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            using (var form = new HotkeyRecorderForm("Ctrl + Q", config.LiveBackdrop))
            {
                form.ShowDialog();
            }

            return 0;
        }

        private static int MenuTest()
        {
            Application.EnableVisualStyles();
            var menu = new GlassMenu();
            menu.AddItem("添加选中文字  Ctrl+Q", () => { });
            menu.AddSeparator();
            menu.AddItem("设置…", () => { });
            menu.AddItem("设置快捷键…", () => { });
            menu.AddSeparator();
            menu.AddItem("打开配置文件", () => { });
            menu.AddItem("打开日志", () => { });
            menu.AddItem("开机自动启动", () => { }, true);
            menu.AddSeparator();
            menu.AddItem("退出", () => { });
            menu.ShowAt(new Point(Ui.S(400), Ui.S(300)));
            Application.Run(new ApplicationContext(menu));
            return 0;
        }

        private static int ExportIcon(string[] args)
        {
            string path = args.Length > 1
                ? args[1]
                : Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "app.ico");
            IconExport.Write(path, new[] { 16, 24, 32, 48, 64, 128, 256 });
            Console.WriteLine(path);
            return 0;
        }

        private static int PickerTest()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            using (var picker = new PickerForm(config, "截止时间", "当前 明天 15:00"))
            {
                picker.Add("today", "今天", "23:59", false);
                picker.Add("tomorrow", "明天", "23:59", true);
                picker.Add("week", "本周日", "23:59", false);
                picker.Add("monday", "下周一", "23:59", false);
                picker.Add("custom", "自定义…", "", false);
                picker.Add("clear", "清除截止时间", "", false);
                picker.Build();
                picker.ShowDialog();
            }

            return 0;
        }

        private static int UpdateTest()
        {
            string feed = "";
            string[] argv = Environment.GetCommandLineArgs();
            for (int index = 0; index < argv.Length - 1; index++)
            {
                if (argv[index] == "--update-test")
                {
                    feed = argv[index + 1];
                }
            }

            Console.WriteLine("current=" + AppInfo.Version + " feed=" + feed);
            UpdateInfo info = UpdateService.Check(feed);
            if (info == null)
            {
                Console.WriteLine("result=none error=" + UpdateService.LastError);
                return 0;
            }

            Console.WriteLine("result=newer version=" + info.Version + " url=" + info.Url);
            return 0;
        }

        private static int DateTest()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            using (var dialog = new DateTimeDialog(config, DateTimeOffset.Now))
            {
                dialog.ShowDialog();
            }

            return 0;
        }

        private static int TimeTest()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            using (var picker = new TimePickerForm(config, 15, 30))
            {
                picker.ShowDialog();
            }

            return 0;
        }

        private static int DetailTest()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            TodoItem sample = new TodoService(config).Load().FirstOrDefault(item => !item.IsCompleted)
                              ?? new TodoItem { Text = "示例待办" };
            using (var detail = new TodoDetailForm(config, sample.Clone(), false))
            {
                detail.ShowDialog();
            }

            return 0;
        }

        private static int EditTest()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            TodoItem sample = new TodoService(config).Load().FirstOrDefault();
            if (sample == null)
            {
                Console.WriteLine("no todo to edit");
                return 1;
            }

            using (var editor = new TodoEditForm(config, sample))
            {
                editor.ShowDialog();
            }

            return 0;
        }

        private static int TodoSelfTest()
        {
            AppConfig config = AppConfig.Load();
            var service = new TodoService(config);
            int failures = 0;

            void Check(string name, bool ok)
            {
                Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
                if (!ok) { failures++; }
            }

            var draft = new TodoDraft
            {
                Title = "自检-每周例会",
                Due = TodoService.DuePreset("today"),
                Steps = new List<string> { "准备周报", "同步进度" },
                Important = true
            };

            TodoItem added = service.Add(draft);
            Check("添加待办", added != null && !string.IsNullOrEmpty(added.Id));

            service.SetRecurrence(added.Id, TodoRecurrenceMode.Weekly);
            TodoItem recurring = service.Load().FirstOrDefault(i => i.Id == added.Id);
            Check("设置每周重复", recurring != null && recurring.Recurrence != null &&
                                 recurring.Recurrence.Mode == TodoRecurrenceMode.Weekly);

            service.SetCompleted(added.Id, true);
            List<TodoItem> afterComplete = service.Load();
            TodoItem done = afterComplete.FirstOrDefault(i => i.Id == added.Id);
            TodoItem generated = done == null || string.IsNullOrEmpty(done.GeneratedNextItemId)
                ? null
                : afterComplete.FirstOrDefault(i => i.Id == done.GeneratedNextItemId);
            Check("完成时生成下一次", generated != null && !generated.IsCompleted && generated.DueDate.HasValue);
            Check("下一次是 7 天后", generated != null && generated.DueDate.HasValue && done.DueDate.HasValue &&
                                    Math.Abs((generated.DueDate.Value - done.DueDate.Value).TotalDays - 7) < 0.01);
            Check("子项被重置", generated != null && generated.Steps.Count == 2 && generated.Steps.All(s => !s.IsCompleted));

            service.SetCompleted(added.Id, false);
            Check("取消完成后移除生成项", service.Load().All(i => generated == null || i.Id != generated.Id));

            TodoItem stepTarget = service.Load().FirstOrDefault(i => i.Id == added.Id);
            if (stepTarget != null && stepTarget.Steps.Count >= 2)
            {
                service.ToggleStep(added.Id, stepTarget.Steps[0].Id);
                service.ToggleStep(added.Id, stepTarget.Steps[1].Id);
                TodoItem auto = service.Load().FirstOrDefault(i => i.Id == added.Id);
                Check("子项全勾选后自动完成", auto != null && auto.IsCompleted);
            }
            else
            {
                Check("子项全勾选后自动完成", false);
            }

            service.Delete(added.Id);
            Check("删除待办", service.Load().All(i => i.Id != added.Id));
            while (service.Undo()) { }

            Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static int TodoTest()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            using (var window = new TodoWindow(config))
            {
                Application.Run(window);
            }

            return 0;
        }

        private static int OnboardingTest()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            using (var form = new OnboardingForm(config, "Alt + Q", (mods, key) => { }))
            {
                form.ShowDialog();
            }

            return 0;
        }

        private static int IconTest()
        {
            string path = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath) ?? ".",
                "tray-icon-test.png");
            using (Icon icon = TrayIconFactory.Create(256))
            using (Bitmap bitmap = icon.ToBitmap())
            {
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            Console.WriteLine(path);
            return 0;
        }

        private static int AiTest()
        {
            AppConfig config = AppConfig.Load();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string result = AiParser.Test(config);
            watch.Stop();
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}  ({1:0.00}s)",
                result,
                watch.Elapsed.TotalSeconds));
            return result.StartsWith("连接成功", StringComparison.Ordinal) ? 0 : 1;
        }

        private static int OpenSettings()
        {
            Application.EnableVisualStyles();
            AppConfig config = AppConfig.Load();
            using (var form = new SettingsForm(config))
            {
                form.ShowDialog();
            }

            return 0;
        }

        private static int WriteTest()
        {
            var report = new StringBuilder();
            Trace("start");
            report.AppendLine("user=" + Environment.UserName + " domain=" + Environment.UserDomainName);
            Trace("identity ok");
            report.AppendLine("exe=" + Application.ExecutablePath);
            report.AppendLine("cwd=" + Environment.CurrentDirectory);
            report.AppendLine("temp=" + Path.GetTempPath());
            report.AppendLine("localappdata=" + Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            Trace("env ok");

            string[] targets =
            {
                Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "wtest_exe.txt"),
                Path.Combine(Path.GetTempPath(), "wtest_temp.txt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskBoxToDo", "wtest_local.txt")
            };

            foreach (string target in targets)
            {
                Trace("begin " + target);
                try
                {
                    string directory = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    Trace("dir ok " + target);
                    File.WriteAllText(target, "write test " + DateTime.Now.ToString("o"), new UTF8Encoding(false));
                    Trace("written " + target);
                    report.AppendLine("OK   " + target);
                }
                catch (Exception ex)
                {
                    Trace("fail " + target + " :: " + ex.GetType().Name + " :: " + ex.Message);
                    report.AppendLine("FAIL " + target + " :: " + ex.GetType().Name + " :: " + ex.Message);
                }
            }

            Trace("done writes");

            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "writetest.txt"),
                    report.ToString(),
                    new UTF8Encoding(false));
            }
            catch
            {
                // Best effort.
            }

            Console.WriteLine(report.ToString());
            return 0;
        }

        private static string Next(string[] args, ref int index)        {
            index++;
            return index < args.Length ? args[index] : null;
        }

        private static string Json(bool ok, TodoDraft draft, ImportResult result, string message, string error)
        {
            var payload = new Dictionary<string, object>
            {
                { "ok", ok },
                { "message", message },
                { "error", error }
            };

            if (draft != null)
            {
                payload["title"] = draft.Title;
                payload["steps"] = draft.Steps.ToArray();
                payload["important"] = draft.Important;
                payload["dueDate"] = draft.Due.HasValue
                    ? draft.Due.Value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)
                    : null;
                payload["dueText"] = TodoParser.Describe(draft.Due);
                payload["notes"] = draft.Notes;
            }

            if (result != null)
            {
                payload["itemId"] = result.ItemId;
                payload["storePath"] = result.StorePath;
                payload["widgetId"] = result.WidgetId;
                payload["deskBoxWasRunning"] = result.DeskBoxWasRunning;
                payload["deskBoxRestarted"] = result.DeskBoxRestarted;
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return PrettyJson.Format(serializer.Serialize(payload));
        }

        private static void Emit(string json, string outPath)
        {
            try
            {
                string diag = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath) ?? ".",
                    "emit.log");
                File.AppendAllText(
                    diag,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:HH:mm:ss} out='{1}' len={2}{3}",
                        DateTime.Now,
                        outPath ?? "<null>",
                        json.Length,
                        Environment.NewLine));
            }
            catch
            {
                // Diagnostics only.
            }

            if (!string.IsNullOrWhiteSpace(outPath))
            {
                try
                {
                    File.WriteAllText(outPath, json, new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    Log.Write("cli out write failed: " + ex.Message);
                    try
                    {
                        File.AppendAllText(
                            Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "emit.log"),
                            "WRITE FAIL " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine);
                    }
                    catch
                    {
                        // Diagnostics only.
                    }
                }
            }

            try
            {
                Console.WriteLine(json);
            }
            catch
            {
                // No console attached.
            }
        }

        private static void AttachConsoleForOutput()
        {
            try
            {
                // Only reach for the parent console when stdout is a real
                // console handle. When the caller redirected stdout we must
                // keep writing to that pipe.
                if (Console.IsOutputRedirected)
                {
                    return;
                }

                if (AttachConsole(-1))
                {
                    var stdout = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8)
                    {
                        AutoFlush = true
                    };
                    Console.SetOut(stdout);
                }
            }
            catch
            {
                // Running without a console is fine; --out still works.
            }
        }

        private const string Usage =
@"DeskBoxToDo —— 一句话 → DeskBox 待办

  图形界面：直接运行 DeskBoxToDo.exe，托盘常驻，选中文字后按快捷键
            （默认 Ctrl+Shift+Q；被占用时可再生效于托盘菜单的「设置快捷键…」）。
  命令行：
    DeskBoxToDo.exe --text ""第五章作业题：1，2（2）①②，2（3）。周五上课记得交。"" [选项]

选项：
    --text <文字>        要解析的原文
    --text-file <文件>   从 UTF-8 文本文件读取原文
    --title <标题>       覆盖解析出的标题
    --steps <a;b;c>      覆盖解析出的子项，用 ; 或 | 分隔
    --due <时间>         覆盖截止时间，如 ""2026-11-20 08:00""
    --important <bool>   覆盖重要标记
    --data-root <目录>   指向另一个 DeskBox 数据根（默认 %LOCALAPPDATA%\DeskBox）
    --dry-run            只解析不写入
    --no-restart         写入但不重启 DeskBox
    --preview            先弹出确认窗口
    --out <文件>         把结果 JSON 写到文件
    --help               显示本帮助

说明：DeskBox 的待办由界面独占写入，直接改文件会被覆盖。本工具会先让
DeskBox 退出，写入 data/widgets/<id>/todo.json，再把它重新启动（约 2 秒）。
";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
    }
}
