using Newtonsoft.Json;
using PcapDotNet.Core;
using SmprMonitoring;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;

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

        public MainWindow()
        {
            InitializeComponent();
            
            using (StreamReader file = File.OpenText("settings.json"))
            {
                JsonSerializer serializer = new JsonSerializer();
                _settings = (Settings)serializer.Deserialize(file, typeof(Settings));
            }

            foreach (var device in _allDevices)
            {
                StringBuilder sb = new StringBuilder();

                var ipv4 = device.Addresses.Where(h => h.Address.Family == SocketAddressFamily.Internet /*|| h.Address.Family == SocketAddressFamily.Internet6*/);

                if (ipv4.Count() == 0) continue;

                for (int i = ipv4.Count()-1; i >= 0; i--)
                {
                    sb.Append(" ");
                    sb.Append(ipv4.ElementAt(i).Address.ToString().Replace("Internet6 ", "").Replace("Internet ",""));
                }

                string name = device.Description.Split('\'')[1];

                DevicesCB.Items.Add(name + ":" + sb.ToString());
                _deviceID.Add(device.Name);

                if (device.Name == _settings.DeviceName)
                {
                    DevicesCB.SelectedIndex = DevicesCB.Items.Count-1;
                }
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
            _settings.Destinations.Remove((Destination)_lbDestinations.SelectedItem);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            string result = _settings.CheckSettings();
            if (result == null)
            {
                using (StreamWriter file = File.CreateText("settings.json"))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Formatting = Formatting.Indented;
                    serializer.Serialize(file, _settings);
                }
            }
            else
            {
                MessageBox.Show("Ошибка при сохранении:\n" + result);
                e.Cancel = true;
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
            IPAddress address;
            if (IPAddress.TryParse(IpAddressTB.Text, out address))
            {
                _settings.AllowedIPAddresses.Add(IpAddressTB.Text);
            }
            else MessageBox.Show("Неверный формат IP-адреса.", "Ошибка!", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        

        private void DevicesCB_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (loadComplete)
            _settings.DeviceName = _deviceID[DevicesCB.SelectedIndex];
        }
    }


}
