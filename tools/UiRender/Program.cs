using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TbtMemoryCleaner.Core;
using ThinkBookToolkit.PluginApi;
using ThinkBookToolkit.PluginUi;
using TbtMemoryCleaner.Ui;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "out/render");
        Directory.CreateDirectory(output);

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var cases = new (string Name, int Width, int Height, bool Dark, string Language)[]
        {
            ("dark-1280", 1280, 760, true, "zh-CN"),
            ("light-1280", 1280, 760, false, "zh-CN"),
            ("dark-900", 900, 760, true, "zh-CN"),
            ("light-en-1280", 1280, 760, false, "en-US"),
            ("dark-tall", 1280, 1500, true, "zh-CN"),
        };

        var failures = 0;

        foreach (var item in cases)
        {
            try
            {
                var page = new MemoryCleanerPage();
                var context = new FakeContext(item.Dark, item.Language);
                var view = page.CreateView(context);
                page.Update(context.State);

                var background = item.Dark ? Color.FromRgb(0x0A, 0x10, 0x20) : Color.FromRgb(0xF4, 0xF7, 0xFB);
                var host = new Border
                {
                    Child = view,
                    Width = item.Width,
                    Height = item.Height,
                    Background = new SolidColorBrush(background),
                };

                host.Measure(new Size(item.Width, item.Height));
                host.Arrange(new Rect(0, 0, item.Width, item.Height));
                host.UpdateLayout();

                var bitmap = new RenderTargetBitmap(item.Width, item.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);

                var ratio = NonBackgroundRatio(bitmap, background);
                var texts = CountTextBlocks(view);
                var file = Path.Combine(output, item.Name + ".png");
                Save(bitmap, file);

                var ok = ratio > 0.02 && texts >= 20;
                Console.WriteLine($"{item.Name}: non-bg={ratio:P1} textBlocks={texts} -> {(ok ? "PASS" : "FAIL")} ({file})");
                if (!ok) failures++;

                if (item.Name == "dark-1280")
                {
                    host.Child = null;
                    var outer = new ScrollViewer
                    {
                        Width = item.Width,
                        Height = item.Height,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = view,
                    };
                    outer.Measure(new Size(item.Width, item.Height));
                    outer.Arrange(new Rect(0, 0, item.Width, item.Height));
                    outer.UpdateLayout();

                    var beforeOffset = outer.VerticalOffset;
                    var target = FindAll<TextBlock>(view).FirstOrDefault(t => (t.Text ?? "").Contains("清理区域")) as UIElement ?? view;
                    target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                    {
                        RoutedEvent = UIElement.MouseWheelEvent,
                    });
                    outer.UpdateLayout();
                    var afterOffset = outer.VerticalOffset;

                    var scrolled = afterOffset > beforeOffset;
                    Console.WriteLine($"interaction: host scroll ({beforeOffset:0.0} -> {afterOffset:0.0}) -> {(scrolled ? "PASS" : "FAIL")}");
                    if (!scrolled) failures++;

                    var areaBox = FindFirst<CheckBox>(view, box => box.Content is null && box.IsEnabled);
                    if (areaBox is null)
                    {
                        Console.WriteLine("interaction: area checkbox not found -> FAIL");
                        failures++;
                    }
                    else
                    {
                        areaBox.IsChecked = false;
                        var wroteAreas = context.Writes.Any(w => w.Id == "areas");
                        Console.WriteLine($"interaction: area toggle writes 'areas' -> {(wroteAreas ? "PASS" : "FAIL")} ({string.Join(",", context.Writes.Select(w => w.Id))})");
                        if (!wroteAreas) failures++;
                    }

                    var button = FindFirst<Button>(view, b => b.Content is string);
                    if (button is null)
                    {
                        Console.WriteLine("interaction: clean button not found -> FAIL");
                        failures++;
                    }
                    else
                    {
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var message = FindAll<TextBlock>(view).Select(t => t.Text ?? "").FirstOrDefault(t => t.Contains("管理员") || t.Contains("administrator"));
                        var clickOk = !string.IsNullOrEmpty(message);
                        Console.WriteLine($"interaction: click without elevation shows guidance -> {(clickOk ? "PASS" : "FAIL")} ({message})");
                        if (!clickOk) failures++;
                    }
                }

                page.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{item.Name}: EXCEPTION {ex.GetBaseException().Message}");
                failures++;
            }
        }

        try
        {
            // public screenshot: elevated fake cleaner (no "not elevated" banner), cropped later
            var publicPage = new MemoryCleanerPage(new FakeCleaner());
            var publicContext = new FakeContext(true, "zh-CN");
            var publicView = publicPage.CreateView(publicContext);
            publicPage.Update(publicContext.State);
            var publicHost = new Border
            {
                Child = publicView,
                Width = 1280,
                Height = 1005,
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x10, 0x20)),
            };
            publicHost.Measure(new Size(1280, 1005));
            publicHost.Arrange(new Rect(0, 0, 1280, 1005));
            publicHost.UpdateLayout();
            var publicBitmap = new RenderTargetBitmap(1280, 1005, 96, 96, PixelFormats.Pbgra32);
            publicBitmap.Render(publicHost);
            Save(publicBitmap, Path.Combine(output, "public-main.png"));
            publicPage.Dispose();
            Console.WriteLine("public-main: saved");

            var lightPage = new MemoryCleanerPage(new FakeCleaner());
            var lightContext = new FakeContext(false, "zh-CN");
            var lightView = lightPage.CreateView(lightContext);
            lightPage.Update(lightContext.State);
            var lightHost = new Border
            {
                Child = lightView,
                Width = 1280,
                Height = 1005,
                Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFB)),
            };
            lightHost.Measure(new Size(1280, 1005));
            lightHost.Arrange(new Rect(0, 0, 1280, 1005));
            lightHost.UpdateLayout();
            var lightBitmap = new RenderTargetBitmap(1280, 1005, 96, 96, PixelFormats.Pbgra32);
            lightBitmap.Render(lightHost);
            Save(lightBitmap, Path.Combine(output, "public-light.png"));
            lightPage.Dispose();
            Console.WriteLine("public-light: saved");

            var page = new MemoryCleanerPage(new FakeCleaner());
            var context = new FakeContext(true, "zh-CN");
            var view = page.CreateView(context);
            page.Update(context.State);

            var background = Color.FromRgb(0x0A, 0x10, 0x20);
            var host = new Border
            {
                Child = view,
                Width = 1280,
                Height = 1100,
                Background = new SolidColorBrush(background),
            };
            host.Measure(new Size(1280, 1100));
            host.Arrange(new Rect(0, 0, 1280, 1100));
            host.UpdateLayout();

            var button = FindFirst<Button>(view, b => b.Content is string);
            button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var populated = PumpUntil(() => FindAll<TextBlock>(view).Any(t => (t.Text ?? "").Contains("工作集（推算）")), 4000);
            var explained = FindAll<TextBlock>(view).Any(t => (t.Text ?? "").Contains("工作集被裁剪"));
            host.UpdateLayout();

            var bitmap = new RenderTargetBitmap(1280, 1100, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            Save(bitmap, Path.Combine(output, "evidence.png"));

            Console.WriteLine($"evidence: populated={populated} explained={explained} -> {(populated && explained ? "PASS" : "FAIL")}");
            if (!populated || !explained) failures++;
        }
        catch (Exception ex)
        {
            Console.WriteLine("evidence: EXCEPTION " + ex.GetBaseException().Message);
            failures++;
        }

        application.Shutdown();
        Console.WriteLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
        return failures == 0 ? 0 : 1;
    }

    private static bool PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(20);
        }

        return condition();
    }

    private static double NonBackgroundRatio(RenderTargetBitmap bitmap, Color background)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        long different = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (Math.Abs(pixels[i] - background.B) > 6 ||
                Math.Abs(pixels[i + 1] - background.G) > 6 ||
                Math.Abs(pixels[i + 2] - background.R) > 6)
                different++;
        }

        return (double)different / (width * height);
    }

    private static int CountTextBlocks(DependencyObject root)
    {
        var count = 0;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is TextBlock) count++;

            var children = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < children; i++) queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }

        return count;
    }

    private static T? FindFirst<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T typed && predicate(typed)) return typed;

            var children = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < children; i++) queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }

        return null;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T typed) yield return typed;

            var children = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < children; i++) queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private sealed class FakeCleaner : IMemoryCleaner
    {
        private static readonly MemorySnapshot Before = new(DateTimeOffset.Now, 27, 33748UL << 20, 23593UL << 20,
            8UL << 30, 40UL << 30, 19630UL << 20, 3953UL << 20, 314UL << 20, 4096, true);

        private static readonly MemorySnapshot After = new(DateTimeOffset.Now, 27, 33748UL << 20, 25691UL << 20,
            8UL << 30, 40UL << 30, 20090UL << 20, 5591UL << 20, 4269UL << 20, 4096, true);

        public bool IsElevated => true;

        public MemoryArea SupportedAreas => MemoryArea.All;

        public string? PrivilegeError => null;

        public MemorySnapshot Sample() => After;

        public CleanReport Clean(MemoryArea areas, CancellationToken cancellationToken = default)
        {
            var outcomes = new List<AreaOutcome>
            {
                new(MemoryArea.WorkingSet, true, 0, "MemoryEmptyWorkingSets"),
                new(MemoryArea.ModifiedFileCache, true, 0, "C:"),
                new(MemoryArea.SystemFileCache, true, 0, "SetSystemFileCacheSize"),
                new(MemoryArea.StandbyLowPriority, true, 0, "MemoryPurgeLowPriorityStandbyList"),
            };
            return new CleanReport(DateTimeOffset.Now, TimeSpan.FromMilliseconds(420), areas, outcomes, Before, After);
        }
    }

    private sealed class FakeContext : IPluginPageContext
    {
        public FakeContext(bool dark, string language)
        {
            var empty = JsonDocument.Parse("{}").RootElement;
            var settings = new Dictionary<string, JsonElement>
            {
                ["areas"] = JsonSerializer.SerializeToElement(15d),
                ["auto-enabled"] = JsonSerializer.SerializeToElement(false),
                ["auto-threshold"] = JsonSerializer.SerializeToElement(85d),
                ["auto-interval"] = JsonSerializer.SerializeToElement(600d),
            };

            var request = new PluginRequest(
                "view",
                new PluginContext(DateTimeOffset.UtcNow, new Dictionary<string, SensorReading>(), empty, empty, Array.Empty<HostOperation>()),
                settings);

            State = new PluginPageState(request, dark, language);
        }

        public List<(string Id, JsonElement Value)> Writes { get; } = new();

        public string PluginId => "tbt.memory.cleaner";
        public string PageId => "tbt.memory.cleaner.page";
        public string PluginDirectory => AppContext.BaseDirectory;
        public PluginPageState State { get; }
        public CancellationToken Lifetime => CancellationToken.None;

        public Task SetSettingAsync(string id, JsonElement value)
        {
            Writes.Add((id, value.Clone()));
            return Task.CompletedTask;
        }

        public Task ExecuteAsync(HostCommand command) => Task.CompletedTask;

        public Task ShowToastAsync(string message, bool isError) => Task.CompletedTask;
    }
}
