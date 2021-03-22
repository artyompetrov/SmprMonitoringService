using Newtonsoft.Json;
using PcapDotNet.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Windows;
using SMPRmonitoring;

namespace Configurator
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Settings _settings;

        bool loadComplete;

        IList<LivePacketDevice> _allDevices = LivePacketDevice.AllLocalMachine;
        List<string> _deviceID = new List<string>();
        string _savedSettings;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                using (StreamReader file = File.OpenText("settings.json"))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    _settings = (Settings) serializer.Deserialize(file, typeof(Settings));
                }
                
                var settingsErrors = _settings.CheckSettings();
                if (settingsErrors != null) throw new Exception($"Настройки содержат следующие ошибки:{Environment.NewLine}{settingsErrors}");
                
                foreach (var device in _allDevices)
                {
                    StringBuilder sb = new StringBuilder();

                    var ipv4 = device.Addresses.Where(
                        h => h.Address.Family ==
                             SocketAddressFamily.Internet /*|| h.Address.Family == SocketAddressFamily.Internet6*/);

                    if (ipv4.Count() == 0) continue;

                    for (int i = ipv4.Count() - 1; i >= 0; i--)
                    {
                        sb.Append(" ");
                        sb.Append(ipv4.ElementAt(i).Address.ToString().Replace("Internet6 ", "")
                            .Replace("Internet ", ""));
                    }

                    string name = device.Description.Split('\'')[1];

                    DevicesCB.Items.Add(name + ":" + sb.ToString());
                    _deviceID.Add(device.Name);

                    if (device.Name == _settings.DeviceName)
                    {
                        DevicesCB.SelectedIndex = DevicesCB.Items.Count - 1;
                    }
                }

            }
            catch (Exception ex)
            {
                var result = MessageBox.Show(ex.Message, "Ошибка при запуске приложения!", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
            }


            using (var sw = new StringWriter())
            {
                var serializer = new JsonSerializer() {Formatting = Formatting.Indented};
                serializer.Serialize(sw, _settings);
                _savedSettings = sw.ToString();
            }
            
            DataContext = _settings;

            loadComplete = true;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            _settings.Destinations.Add(new Destination("Новое направление", 0, 0));
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            var selectedItem = (Destination)_lbDestinations.SelectedItem;
            var result = MessageBox.Show($"Вы действительно хотите удалить направление: {selectedItem}", "Удаление направления.", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                _settings.Destinations.Remove(selectedItem);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!loadComplete) return;

            string currentSettings;
            using (var sw = new StringWriter())
            {
                var serializer = new JsonSerializer() {Formatting = Formatting.Indented};
                serializer.Serialize(sw, _settings);
                currentSettings = sw.ToString();
            }
            
            if (_savedSettings != currentSettings)
            {
                var result = MessageBox.Show("Сохранить изменения?", "Изменения не сохранены", MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    SaveSettings(false);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _settings.Destinations.ResetBindings();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            if (IpAdressesLB.SelectedIndex > -1)
                _settings.AllowedIPAddresses.RemoveAt(IpAdressesLB.SelectedIndex);
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            try
            {
                var allowedIpList = (IList<Ip>)IpAdressesLB.ItemsSource;

                var newIp = new Ip(IpAddressTB.Text);

                if (!allowedIpList.Contains(newIp))
                {
                    allowedIpList.Add(newIp);

                    IpAddressTB.Text = string.Empty;
                }
                else
                {
                    throw new Exception("Данный IP уже есть в списке.");
                }

            }
            catch (Exception exception)
            {
                MessageBox.Show($"Не удалось добавить IP-адрес. Ошибка: {exception.Message}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        

        private void DevicesCB_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (loadComplete)
                _settings.DeviceName = _deviceID[DevicesCB.SelectedIndex];
        }


        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void SaveSettings(bool showMessage = true)
        {
            string result = _settings.CheckSettings();
            if (result == null)
            {
                string settingsToSave;
                using (var sw = new StringWriter())
                {
                    var serializer = new JsonSerializer() { Formatting = Formatting.Indented };
                    serializer.Serialize(sw, _settings);
                    settingsToSave = sw.ToString();
                }

                File.WriteAllText("settings.json", settingsToSave);

                _savedSettings = settingsToSave;

                if (showMessage)
                {
                    MessageBox.Show("Конфигурация сохранена", "Сохранено!", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show($"Ошибка при сохранении:{Environment.NewLine}" + result);
            }
        }

        private void AddDestinationIpButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var newIp = new Ip(TbNewDestinationIp.Text);

                var found = false;
                foreach (var destination in _settings.Destinations)
                {
                    foreach (var ipAddress in destination.IpAddresses)
                    {
                        if (!Equals(newIp, ipAddress)) continue;

                        found = true;
                        break;
                    }

                    if (found) break;
                }
                
                if (!found)
                {
                    ((IList<Ip>) LbDestinationIp.ItemsSource).Add(new Ip(TbNewDestinationIp.Text));

                    TbNewDestinationIp.Text = string.Empty;
                }
                else
                {
                    throw new Exception("Данный IP используется");
                }

            }
            catch (Exception exception)
            {
                MessageBox.Show($"Не удалось добавить IP-адрес. Ошибка: {exception.Message}", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
        }

        private void RemoveDestinationIpButton_Click(object sender, RoutedEventArgs e)
        {
            if (LbDestinationIp.SelectedIndex > -1)
                ((Destination)_lbDestinations.SelectedItem).IpAddresses.RemoveAt(LbDestinationIp.SelectedIndex);
        }

        private void UDPRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PortLabel.Content = "Порт (dst)";
        }

        private void TCPRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PortLabel.Content = "Порт (src)";
        }
    }


}
