using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Newtonsoft.Json;

namespace PoeAncientsPriceHelper;

/// <summary>
/// PaddleOCR 识别服务 - 调用 Python PaddleOCR 进行文字识别
/// </summary>
internal sealed class PaddleOcrService : IDisposable
{
    private readonly string _pythonPath;
    private readonly string _scriptPath;
    private readonly Action<string>? _log;
    private bool _initialized;
    
    public PaddleOcrService(Action<string>? log = null)
    {
        _log = log;
        
        // 查找 Python 路径
        _pythonPath = FindPythonPath();
        
        // 设置脚本路径
        _scriptPath = Path.Combine(AppContext.BaseDirectory, "paddle_ocr.py");
        
        // 检查脚本是否存在
        if (!File.Exists(_scriptPath))
        {
            Log("[警告] PaddleOCR 脚本不存在，将使用 Tesseract OCR");
            _initialized = false;
        }
        else
        {
            _initialized = true;
            Log($"[信息] PaddleOCR 服务初始化完成: Python={_pythonPath}");
        }
    }
    
    /// <summary>
    /// 查找 Python 路径
    /// </summary>
    private string FindPythonPath()
    {
        // 尝试常见的 Python 路径
        var possiblePaths = new[]
        {
            "python",
            "python3",
            @"C:\Python310\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python312\python.exe",
            @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Python\Python310\python.exe",
            @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Python\Python311\python.exe",
            @"C:\Users\" + Environment.UserName + @"\AppData\Local\Programs\Python\Python312\python.exe",
        };
        
        foreach (var path in possiblePaths)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode == 0 && output.Contains("Python"))
                {
                    Log($"[信息] 找到 Python: {path} - {output.Trim()}");
                    return path;
                }
            }
            catch
            {
                // 继续尝试下一个路径
            }
        }
        
        Log("[警告] 未找到 Python，PaddleOCR 将不可用");
        return "python"; // 默认值
    }
    
    /// <summary>
    /// 检查 PaddleOCR 是否可用
    /// </summary>
    public bool IsAvailable => _initialized && File.Exists(_scriptPath);
    
    /// <summary>
    /// 使用 PaddleOCR 识别图片
    /// </summary>
    public async Task<PaddleOcrResult> RecognizeAsync(Bitmap image)
    {
        if (!IsAvailable)
        {
            return new PaddleOcrResult { Success = false, Message = "PaddleOCR 不可用" };
        }
        
        try
        {
            // 将图片转换为 base64
            var base64Image = ImageToBase64(image);
            
            // 调用 Python 脚本
            var result = await RunPythonScript(base64Image);
            
            return result;
        }
        catch (Exception ex)
        {
            Log($"[错误] PaddleOCR 识别失败: {ex.Message}");
            return new PaddleOcrResult { Success = false, Message = ex.Message };
        }
    }
    
    /// <summary>
    /// 运行 Python 脚本
    /// </summary>
    private async Task<PaddleOcrResult> RunPythonScript(string base64Image)
    {
        var tcs = new TaskCompletionSource<PaddleOcrResult>();
        
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = $"\"{_scriptPath}\" \"{base64Image}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();
            
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };
            
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };
            
            process.Exited += (sender, e) =>
            {
                var output = outputBuilder.ToString().Trim();
                var error = errorBuilder.ToString().Trim();
                
                if (!string.IsNullOrEmpty(error))
                {
                    Log($"[警告] PaddleOCR stderr: {error}");
                }
                
                try
                {
                    var result = JsonConvert.DeserializeObject<PaddleOcrResult>(output);
                    if (result != null)
                    {
                        tcs.SetResult(result);
                    }
                    else
                    {
                        tcs.SetResult(new PaddleOcrResult { Success = false, Message = "解析结果失败" });
                    }
                }
                catch (Exception ex)
                {
                    Log($"[错误] 解析 PaddleOCR 输出失败: {ex.Message}, 输出: {output}");
                    tcs.SetResult(new PaddleOcrResult { Success = false, Message = $"解析输出失败: {ex.Message}" });
                }
                
                process.Dispose();
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // 设置超时
            var timeout = TimeSpan.FromSeconds(30);
            var timeoutTask = Task.Delay(timeout);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                try
                {
                    process.Kill();
                }
                catch { }
                
                return new PaddleOcrResult { Success = false, Message = "识别超时" };
            }
            
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            Log($"[错误] 运行 Python 脚本失败: {ex.Message}");
            return new PaddleOcrResult { Success = false, Message = ex.Message };
        }
    }
    
    /// <summary>
    /// 将图片转换为 base64 字符串
    /// </summary>
    private string ImageToBase64(Bitmap image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        var bytes = ms.ToArray();
        return Convert.ToBase64String(bytes);
    }
    
    /// <summary>
    /// 日志方法
    /// </summary>
    private void Log(string message)
    {
        _log?.Invoke(message);
    }
    
    public void Dispose()
    {
        // 清理资源
    }
}

/// <summary>
/// PaddleOCR 识别结果
/// </summary>
internal sealed class PaddleOcrResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<PaddleOcrItem> Items { get; set; } = new();
    public int Count { get; set; }
}

/// <summary>
/// PaddleOCR 识别的项目
/// </summary>
internal sealed class PaddleOcrItem
{
    public string Text { get; set; } = "";
    public float Confidence { get; set; }
    public int CenterY { get; set; }
}
