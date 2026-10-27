using System.Text;

namespace TcpDebugAssistant.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

        var form = new MainForm();

        var selfTest = args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
        var shotIndex = Array.FindIndex(args, a => a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase));
        var darkShot = args.Any(a => a.Equals("--dark", StringComparison.OrdinalIgnoreCase));

        if (darkShot && !selfTest && shotIndex < 0)
        {
            form.Shown += (_, _) =>
            {
                Theme.Toggle();
                form.ApplyTheme();
            };
        }

        if (selfTest)
        {
            form.Shown += async (_, _) =>
            {
                var result = await form.RunSelfTestAsync();
                var output = Path.Combine(AppContext.BaseDirectory, "self-test-result.txt");
                try
                {
                    File.WriteAllText(output, result.Log, new UTF8Encoding(true));
                }
                catch
                {
                    // 写文件失败时仍然输出到控制台。
                }

                try
                {
                    Console.Out.Write(result.Log);
                    Console.Out.Write(output);
                    Console.Out.Flush();
                }
                catch
                {
                    // 无控制台时忽略。
                }

                Environment.ExitCode = result.Failed == 0 ? 0 : 1;
                form.Close();
            };
        }
        else if (shotIndex >= 0 && shotIndex + 1 < args.Length)
        {
            var path = args[shotIndex + 1];
            var serverShot = args.Any(a => a.Equals("--server", StringComparison.OrdinalIgnoreCase));

            form.Shown += async (_, _) =>
            {
                if (darkShot)
                {
                    Theme.Toggle();
                    form.ApplyTheme();
                    await Task.Delay(150);
                }

                if (serverShot)
                {
                    form.SelectServerMode();
                    await Task.Delay(500);
                }

                form.ApplyDemoScenario(serverShot);
                await Task.Delay(700);
                try
                {
                    using var bmp = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    Console.Out.Write("saved: " + path);
                }
                catch (Exception ex)
                {
                    Console.Out.Write("screenshot failed: " + ex.Message);
                }

                form.Close();
            };
        }

        Application.Run(form);
    }

    private static void ShowFatal(Exception? ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), DateTime.Now + " " + ex + Environment.NewLine);
        }
        catch
        {
            // 日志写入失败忽略。
        }

        try
        {
            MessageBox.Show(
                ex?.ToString() ?? "未知错误",
                "TCP 调试助手 - 未处理异常",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // 弹窗失败时忽略。
        }
    }
}
