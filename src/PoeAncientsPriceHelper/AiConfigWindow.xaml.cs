using System.Windows;

namespace PoeAncientsPriceHelper;

public partial class AiConfigWindow : Window
{
    private readonly AiRecognitionService _aiService;
    
    public AiConfigWindow()
    {
        InitializeComponent();
        
        _aiService = new AiRecognitionService();
        
        // 加载当前配置
        LoadCurrentConfig();
    }
    
    private void LoadCurrentConfig()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "ai_config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<AiConfig>(json);
                if (config != null)
                {
                    EnableAiCheckBox.IsChecked = config.Enabled;
                    UsePaddleOcrCheckBox.IsChecked = config.UsePaddleOcr;
                    ApiEndpointTextBox.Text = config.ApiEndpoint ?? "https://api.openai.com/v1/chat/completions";
                    ApiKeyPasswordBox.Password = config.ApiKey ?? "";
                    ModelTextBox.Text = config.Model ?? "gpt-4o-mini";
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"加载配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = new AiConfig
            {
                Enabled = EnableAiCheckBox.IsChecked ?? false,
                UsePaddleOcr = UsePaddleOcrCheckBox.IsChecked ?? false,
                ApiEndpoint = ApiEndpointTextBox.Text?.Trim() ?? "",
                ApiKey = ApiKeyPasswordBox.Password?.Trim() ?? "",
                Model = ModelTextBox.Text?.Trim() ?? "gpt-4o-mini"
            };
            
            var configPath = Path.Combine(AppContext.BaseDirectory, "ai_config.json");
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(configPath, json);
            
            System.Windows.MessageBox.Show("配置已保存，重启程序后生效", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
