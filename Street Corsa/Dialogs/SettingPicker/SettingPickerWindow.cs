using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StreetCorsa.Mods;
using StreetCorsa.Services.Text;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;

namespace StreetCorsa.Dialogs.SettingPicker
{
    /// <summary>
    /// The setting to play, asked at start-up when several are installed and none was picked. It comes up before any
    /// content is read, the look with it, so it is a plain window of its own in the core's words, not one of the
    /// game's dialogs.
    /// </summary>
    public sealed class SettingPickerWindow : Window
    {
        private readonly ListBox _list;

        private SettingPickerWindow(IReadOnlyList<ModInfo> settings, GameText words)
        {
            Title = words.Get("startup.pick_setting.title");
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 14;

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = words.Get("startup.pick_setting"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            _list = new ListBox
            {
                BorderThickness = new Thickness(0),
                MaxHeight = 360
            };
            foreach (var setting in settings) _list.Items.Add(Item(setting));
            _list.SelectedIndex = 0;
            _list.MouseDoubleClick += (_, _) => Choose();
            panel.Children.Add(_list);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var play = new Button { Content = words.Get("startup.pick_setting.play"), IsDefault = true, MinWidth = 100, Padding = new Thickness(12, 4, 12, 4) };
            play.Click += (_, _) => Choose();
            var quit = new Button { Content = words.Get("startup.pick_setting.quit"), IsCancel = true, MinWidth = 100, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(10, 0, 0, 0) };
            buttons.Children.Add(play);
            buttons.Children.Add(quit);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += (_, _) => _list.Focus();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Choose();
            };
        }

        /// <summary>The setting picked in a window of its own; null when the player quit instead</summary>
        public static ModInfo? Ask(IReadOnlyList<ModInfo> settings, GameText words)
        {
            var window = new SettingPickerWindow(settings, words);
            return window.ShowDialog() == true ? (window._list.SelectedItem as ListBoxItem)?.Tag as ModInfo : null;
        }

        private void Choose()
        {
            if (_list.SelectedItem == null) return;
            DialogResult = true;
        }

        private ListBoxItem Item(ModInfo setting)
        {
            var text = new StackPanel { Margin = new Thickness(4, 6, 4, 6) };
            text.Children.Add(new TextBlock { Text = setting.Name, FontWeight = FontWeights.Bold, FontSize = 16 });
            if (!string.IsNullOrWhiteSpace(setting.Description))
                text.Children.Add(new TextBlock { Text = setting.Description, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, MaxWidth = 480 });
            return new ListBoxItem { Content = text, Tag = setting };
        }
    }
}
