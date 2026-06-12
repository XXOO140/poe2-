using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PoeAncientsPriceHelper;

/// <summary>
/// AI 图片识别服务 - 支持 OpenAI 兼容协议
/// </summary>
internal sealed class AiRecognitionService : IDisposable
{
    private readonly HttpClient _http;
    private string _apiEndpoint;
    private string _apiKey;
    private string _model;
    private bool _enabled;
    private bool _usePaddleOcr;
    
    // 日志
    private static readonly string LogDir = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LogFilePath = Path.Combine(LogDir, "ai_recognition.log");
    private static readonly object LogLock = new object();
    
    public bool IsEnabled => _enabled && !string.IsNullOrEmpty(_apiEndpoint) && !string.IsNullOrEmpty(_apiKey);
    
    public AiRecognitionService()
    {
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
        _apiEndpoint = "";
        _apiKey = "";
        _model = "gpt-4o-mini";
        _enabled = false;
        
        LoadConfig();
    }
    
    /// <summary>
    /// 加载 AI 配置
    /// </summary>
    private void LoadConfig()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "ai_config.json");
            if (!File.Exists(configPath))
            {
                Log("[信息] AI 配置文件不存在，使用默认配置");
                SaveConfig(); // 创建默认配置文件
                return;
            }
            
            var json = File.ReadAllText(configPath);
            var config = JsonConvert.DeserializeObject<AiConfig>(json);
            if (config != null)
            {
                _apiEndpoint = config.ApiEndpoint ?? "";
                _apiKey = config.ApiKey ?? "";
                _model = config.Model ?? "gpt-4o-mini";
                _enabled = config.Enabled;
                _usePaddleOcr = config.UsePaddleOcr;
                Log($"[信息] AI 配置已加载: Endpoint={_apiEndpoint}, Model={_model}, Enabled={_enabled}, UsePaddleOcr={_usePaddleOcr}");
            }
        }
        catch (Exception ex)
        {
            Log($"[错误] 加载 AI 配置失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 保存 AI 配置
    /// </summary>
    public void SaveConfig()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "ai_config.json");
            var config = new AiConfig
            {
                ApiEndpoint = _apiEndpoint,
                ApiKey = _apiKey,
                Model = _model,
                Enabled = _enabled,
                UsePaddleOcr = _usePaddleOcr
            };
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(configPath, json);
            Log("[信息] AI 配置已保存");
        }
        catch (Exception ex)
        {
            Log($"[错误] 保存 AI 配置失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 更新 AI 配置
    /// </summary>
    public void UpdateConfig(string apiEndpoint, string apiKey, string model, bool enabled, bool usePaddleOcr = false)
    {
        _apiEndpoint = apiEndpoint;
        _apiKey = apiKey;
        _model = model;
        _enabled = enabled;
        _usePaddleOcr = usePaddleOcr;
        SaveConfig();
    }
    
    /// <summary>
    /// 识别图片中的物品
    /// </summary>
    public async Task<AiRecognitionResult> RecognizeItemsAsync(Bitmap image)
    {
        if (!IsEnabled)
        {
            return new AiRecognitionResult { Success = false, Message = "AI 识别未启用" };
        }
        
        try
        {
            // 将图片转换为 base64
            var base64Image = ImageToBase64(image);
            
            // 构建请求
            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = @"你是一个 Path of Exile 2 游戏物品识别助手。请识别图片中的游戏物品名称和数量。
返回格式为 JSON 数组，每个元素包含：
- name: 物品名称（保持原始语言，简体或繁体）
- quantity: 数量（如果没有数量则为 1）
- english_name: 英文名称（如果能识别）

示例返回：
[{""name"":""神圣石"",""quantity"":1,""english_name"":""Divine Orb""},{""name"":""混沌石"",""quantity"":5,""english_name"":""Chaos Orb""}]"
                },
                new
                {
                    role = "user",
                    content = new List<object>
                    {
                        new { type = "text", text = "请识别这张图片中的所有游戏物品，返回 JSON 格式" },
                        new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                    }
                }
            };
            
            var request = new
            {
                model = _model,
                messages = messages,
                max_tokens = 1000
            };
            
            var requestJson = JsonConvert.SerializeObject(request);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            
            // 发送请求
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            
            var response = await _http.PostAsync(_apiEndpoint, content);
            var responseJson = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Log($"[错误] AI API 返回错误: {response.StatusCode} - {responseJson}");
                return new AiRecognitionResult { Success = false, Message = $"API 错误: {response.StatusCode}" };
            }
            
            // 解析响应
            var responseObject = JObject.Parse(responseJson);
            var choices = responseObject["choices"] as JArray;
            if (choices == null || choices.Count == 0)
            {
                Log($"[错误] AI API 响应格式错误: {responseJson}");
                return new AiRecognitionResult { Success = false, Message = "响应格式错误" };
            }
            
            var messageContent = choices[0]?["message"]?["content"]?.Value<string>();
            if (string.IsNullOrEmpty(messageContent))
            {
                Log("[错误] AI API 响应内容为空");
                return new AiRecognitionResult { Success = false, Message = "响应内容为空" };
            }
            
            Log($"[信息] AI 响应: {messageContent}");
            
            // 解析物品列表
            var items = ParseItemsFromResponse(messageContent);
            return new AiRecognitionResult { Success = true, Items = items, RawResponse = messageContent };
        }
        catch (Exception ex)
        {
            Log($"[错误] AI 识别失败: {ex.Message}");
            return new AiRecognitionResult { Success = false, Message = ex.Message };
        }
    }
    
    /// <summary>
    /// 从 AI 响应中解析物品列表
    /// </summary>
    private List<AiRecognizedItem> ParseItemsFromResponse(string response)
    {
        var items = new List<AiRecognizedItem>();
        
        try
        {
            // 尝试提取 JSON 部分
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var jsonArray = JArray.Parse(jsonStr);
                
                foreach (var item in jsonArray)
                {
                    var name = item["name"]?.Value<string>() ?? "";
                    var quantity = item["quantity"]?.Value<int>() ?? 1;
                    var englishName = item["english_name"]?.Value<string>() ?? "";
                    
                    if (!string.IsNullOrEmpty(name))
                    {
                        items.Add(new AiRecognizedItem
                        {
                            Name = name,
                            Quantity = quantity,
                            EnglishName = englishName
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"[错误] 解析 AI 响应失败: {ex.Message}");
        }
        
        return items;
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
    private static void Log(string message)
    {
        var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(logLine);
        try
        {
            lock (LogLock)
            {
                if (!Directory.Exists(LogDir))
                    Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
            }
        }
        catch { }
    }
    
    public void Dispose()
    {
        _http?.Dispose();
    }
}

/// <summary>
/// AI 配置
/// </summary>
internal sealed class AiConfig
{
    public string ApiEndpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public bool Enabled { get; set; } = false;
    public bool UsePaddleOcr { get; set; } = false;
    public string OcrEngine { get; set; } = "Tesseract"; // Tesseract, PaddleOCR, AI
}

/// <summary>
/// AI 识别结果
/// </summary>
internal sealed class AiRecognitionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<AiRecognizedItem> Items { get; set; } = new();
    public string RawResponse { get; set; } = "";
}

/// <summary>
/// AI 识别的物品
/// </summary>
internal sealed class AiRecognizedItem
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string EnglishName { get; set; } = "";
}
