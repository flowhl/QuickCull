using ImageCullingTool.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCullingTool.Core.Services.Settings
{
    public static class SettingsService
    {
        public static SettingsModel Settings { get; set; }
        public static void LoadSettings()
        {
            string path = FileServiceProvider.FileService.GetSettingsFilePath();
            if (string.IsNullOrEmpty(path) || !FileServiceProvider.FileService.FileExistsAsync(path).Result)
            {
                Settings = new SettingsModel();
                return;
            }
            try
            {
                string json = System.IO.File.ReadAllText(path);
                Settings = System.Text.Json.JsonSerializer.Deserialize<SettingsModel>(json);
                if (Settings == null)
                {
                    Settings = new SettingsModel();
                }
            }
            catch (Exception ex)
            {
                // Log the exception or handle it as needed
                Console.WriteLine($"Error loading settings: {ex.Message}");
                Settings = new SettingsModel();
            }
        }
        public static void SaveSettings()
        {
            string path = FileServiceProvider.FileService.GetSettingsFilePath();
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException("Settings file path is not set.");
            }
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(Settings);
                System.IO.File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                // Log the exception or handle it as needed
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}
