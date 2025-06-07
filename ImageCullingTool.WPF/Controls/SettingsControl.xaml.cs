using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ImageCullingTool.Core.Services.Settings;
using ImageCullingTool.Core.Models;
using ImageCullingTool.Core.Services;

namespace ImageCullingTool.WPF.Controls
{
    /// <summary>
    /// Interaction logic for SettingsControl.xaml
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        private readonly Dictionary<PropertyInfo, object> _originalValues = new Dictionary<PropertyInfo, object>();
        private readonly Dictionary<PropertyInfo, Control> _controlMappings = new Dictionary<PropertyInfo, Control>();
        
        public SettingsControl()
        {
            InitializeComponent();
            BuildSettingsUI();
            LoadSettings();
        }

        private void BuildSettingsUI()
        {
            SettingsStackPanel.Children.Clear();
            _controlMappings.Clear();

            var settingsType = typeof(SettingsModel);
            var properties = settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(p => p.CanRead && p.CanWrite);

            foreach (var property in properties)
            {
                var control = CreateControlForProperty(property);
                if (control != null)
                {
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    // Create label with formatted property name
                    var label = new TextBlock
                    {
                        Text = FormatPropertyName(property.Name) + ":",
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0)
                    };

                    Grid.SetColumn(label, 0);
                    Grid.SetColumn(control, 1);

                    grid.Children.Add(label);
                    grid.Children.Add(control);

                    SettingsStackPanel.Children.Add(grid);
                    _controlMappings[property] = control;
                }
            }
        }

        private Control CreateControlForProperty(PropertyInfo property)
        {
            var propertyType = property.PropertyType;

            if (propertyType == typeof(bool))
            {
                var checkBox = new CheckBox
                {
                    VerticalAlignment = VerticalAlignment.Center
                };
                checkBox.Checked += (s, e) => OnSettingChanged(property, checkBox.IsChecked ?? false);
                checkBox.Unchecked += (s, e) => OnSettingChanged(property, checkBox.IsChecked ?? false);
                return checkBox;
            }
            else if (propertyType == typeof(string))
            {
                var textBox = new TextBox
                {
                    MinWidth = 200,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textBox.TextChanged += (s, e) => OnSettingChanged(property, textBox.Text);
                return textBox;
            }
            else if (propertyType == typeof(int))
            {
                var textBox = new TextBox
                {
                    MinWidth = 100,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Add input validation for integers
                textBox.PreviewTextInput += (s, e) =>
                {
                    e.Handled = !IsValidIntegerInput(((TextBox)s).Text + e.Text);
                };

                textBox.TextChanged += (s, e) =>
                {
                    if (int.TryParse(textBox.Text, out int value))
                    {
                        OnSettingChanged(property, value);
                    }
                };

                return textBox;
            }
            else if (propertyType == typeof(double) || propertyType == typeof(float))
            {
                var textBox = new TextBox
                {
                    MinWidth = 100,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Add input validation for numbers
                textBox.PreviewTextInput += (s, e) =>
                {
                    e.Handled = !IsValidNumericInput(((TextBox)s).Text + e.Text);
                };

                textBox.TextChanged += (s, e) =>
                {
                    if (propertyType == typeof(double) && double.TryParse(textBox.Text, out double doubleValue))
                    {
                        OnSettingChanged(property, doubleValue);
                    }
                    else if (propertyType == typeof(float) && float.TryParse(textBox.Text, out float floatValue))
                    {
                        OnSettingChanged(property, floatValue);
                    }
                };

                return textBox;
            }

            return null; // Unsupported property type
        }

        private string FormatPropertyName(string propertyName)
        {
            // Convert PascalCase to "Pascal Case"
            return Regex.Replace(propertyName, "([A-Z])", " $1").Trim();
        }

        private bool IsValidIntegerInput(string input)
        {
            return string.IsNullOrEmpty(input) || int.TryParse(input, out _);
        }

        private bool IsValidNumericInput(string input)
        {
            return string.IsNullOrEmpty(input) || double.TryParse(input, out _);
        }

        private void OnSettingChanged(PropertyInfo property, object value)
        {
            if (SettingsService.Settings != null)
            {
                property.SetValue(SettingsService.Settings, value);
            }
        }

        private void LoadSettings()
        {
            SettingsService.LoadSettings();
            if (SettingsService.Settings == null) return;

            // Store original values for cancel functionality
            _originalValues.Clear();
            var settingsType = typeof(SettingsModel);
            var properties = settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(p => p.CanRead && p.CanWrite);

            foreach (var property in properties)
            {
                var value = property.GetValue(SettingsService.Settings);
                _originalValues[property] = value;

                // Update the corresponding control
                if (_controlMappings.TryGetValue(property, out var control))
                {
                    UpdateControlValue(control, property.PropertyType, value);
                }
            }
        }

        private void UpdateControlValue(Control control, Type propertyType, object value)
        {
            if (propertyType == typeof(bool) && control is CheckBox checkBox)
            {
                checkBox.IsChecked = (bool)value;
            }
            else if (propertyType == typeof(string) && control is TextBox stringTextBox)
            {
                stringTextBox.Text = value?.ToString() ?? string.Empty;
            }
            else if ((propertyType == typeof(int) || propertyType == typeof(double) || propertyType == typeof(float))
                     && control is TextBox numericTextBox)
            {
                numericTextBox.Text = value?.ToString() ?? "0";
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SettingsService.SaveSettings();
                MessageBox.Show("Settings saved successfully!", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            // Restore original values
            if (SettingsService.Settings != null)
            {
                foreach (var kvp in _originalValues)
                {
                    kvp.Key.SetValue(SettingsService.Settings, kvp.Value);

                    // Update the corresponding control
                    if (_controlMappings.TryGetValue(kvp.Key, out var control))
                    {
                        UpdateControlValue(control, kvp.Key.PropertyType, kvp.Value);
                    }
                }
            }
        }
    }
}