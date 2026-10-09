#nullable enable
using N_m3u8DL_RE_GUI.Core;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Implementation of download service using N_m3u8DL-RE executable or arbitrary processes.
/// Owns process lifecycle and process-tree cancellation safety.
/// </summary>
public class DownloadService : IDownloadService
{
    private Process? _currentProcess;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly object _lockObject = new();

    private static bool SafeIsRunning(Process? process)
    {
        if (process == null) return false;
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public bool IsDownloading
    {
        get
        {
            lock (_lockObject)
            {
                return SafeIsRunning(_currentProcess);
            }
        }
    }

    public Task<bool> StartDownloadAsync(
        DownloadOptions options,
        IProgress<int>? progressCallback = null,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("下载已在进行中，请等待其完成。");
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(options.Input))
        {
            logCallback?.Invoke("请先输入要下载的链接。");
            return Task.FromResult(false);
        }

        var exePath = string.IsNullOrWhiteSpace(options.ExePath) ? "N_m3u8DL-RE.exe" : options.ExePath;
        if (!System.IO.File.Exists(exePath))
        {
            logCallback?.Invoke($"未找到文件：{exePath}");
            logCallback?.Invoke("请从以下地址下载 N_m3u8DL-RE.exe：https://github.com/nilaoda/N_m3u8DL-RE/releases");
            return Task.FromResult(false);
        }

        logCallback?.Invoke("开始下载…");
        var args = ArgsBuilder.Build(options);
        logCallback?.Invoke($"命令：{exePath} {args}");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        return StartTrackedProcessAsync(startInfo, logCallback, progressCallback, redirect: true, cancellationToken);
    }

    public Task<bool> StartProcessAsync(
        string fileName,
        string arguments,
        Action<string>? logCallback = null,
        IProgress<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("已有进程正在运行，请等待其完成。");
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            logCallback?.Invoke("必须指定进程目标文件路径。");
            return Task.FromResult(false);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        return StartTrackedProcessAsync(startInfo, logCallback, progressCallback, redirect: true, cancellationToken);
    }

    private async Task<bool> StartTrackedProcessAsync(
        ProcessStartInfo startInfo,
        Action<string>? logCallback,
        IProgress<int>? progressCallback,
        bool redirect,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        CancellationTokenSource? cts = null;

        lock (_lockObject)
        {
            if (SafeIsRunning(_currentProcess))
            {
                logCallback?.Invoke("已有进程正在运行，请等待其完成。");
                return false;
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokenSource = cts;

            process = new Process { StartInfo = startInfo };
            _currentProcess = process;
        }

        try
        {
            if (redirect)
            {
                process.OutputDataReceived += (_, e) => Forward(e.Data, logCallback, progressCallback);
                process.ErrorDataReceived += (_, e) => Forward(e.Data, logCallback, progressCallback);
            }

            if (!process.Start())
            {
                logCallback?.Invoke($"启动进程失败：{startInfo.FileName}");
                return false;
            }

            if (redirect)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("进程执行已取消。");
                return false;
            }

            var success = process.ExitCode == 0;
            logCallback?.Invoke(success
                ? "进程成功完成！"
                : $"进程退出，代码：{process.ExitCode}");

            if (success)
                progressCallback?.Report(100);

            return success;
        }
        catch (OperationCanceledException)
        {
            logCallback?.Invoke("进程执行已取消。");
            return false;
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"进程执行错误：{ex.Message}");
            return false;
        }
        finally
        {
            lock (_lockObject)
            {
                if (_currentProcess == process) _currentProcess = null;
                if (_cancellationTokenSource == cts) _cancellationTokenSource = null;
            }

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                try { process.Dispose(); } catch { }
            }

            try { cts?.Dispose(); } catch { }
        }
    }

    private static void Forward(string? raw, Action<string>? logCallback, IProgress<int>? progressCallback)
    {
        if (raw == null) return;   // null marks end of stream

        var percent = ConsoleOutputParser.TryExtractPercent(raw);
        if (percent.HasValue)
            progressCallback?.Report(percent.Value);

        var cleaned = ConsoleOutputParser.Clean(raw);
        if (cleaned.Length > 0)
            logCallback?.Invoke(cleaned);
    }

    public void StopDownload()
    {
        Process? procToKill = null;
        CancellationTokenSource? ctsToCancel = null;

        lock (_lockObject)
        {
            procToKill = _currentProcess;
            ctsToCancel = _cancellationTokenSource;
        }

        if (ctsToCancel != null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Token cancel error: {ex.Message}");
            }
        }

        if (procToKill != null)
        {
            try
            {
                if (SafeIsRunning(procToKill))
                {
                    // Kill the entire process tree to also terminate child processes
                    // (ffmpeg, mp4decrypt, python, etc.)
                    procToKill.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop process tree: {ex.Message}");
            }
        }
    }
}
