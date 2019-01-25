using SmprMonitoring;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Configurator
{
    /// <summary>
    /// Логика взаимодействия для SqlScriptGeneratorWindow.xaml
    /// </summary>
    public partial class SqlScriptGeneratorWindow : Window
    {
        private Settings _settings;
        private Destination _destination;

        public uint RTUID
        {
            get
            {
                return _settings.RTUID;
            }
            set
            {
                _settings.RTUID = value;
            }
        }
        public uint TiID { get; set; }



        public SqlScriptGeneratorWindow(Settings settings, int destinationPosition)
        {
            InitializeComponent();

            _settings = settings;
            _destination = settings.Destinations[destinationPosition];

            DataContext = this;

            Generate();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Generate();
        }

        private void Generate()
        {
            string sqlQuery = string.Format(@"DECLARE @StartID int = {0};", TiID) + Environment.NewLine + Environment.NewLine;

            sqlQuery += string.Format(@"DECLARE @Ti1ID int = @StartID + {0};" + Environment.NewLine, 0);
            sqlQuery += string.Format(@"UPDATE [OIKEDIT].[dbo].[AllTI] SET Name = 'Мониторинг СМПР {0} Статус секунды', OutOfWork = 1 WHERE ID = @Ti1ID;" + Environment.NewLine, _destination.Name);
            sqlQuery += string.Format(@"UPDATE [OIKEDIT].[dbo].[DefTI] SET RTUID = {0}, Addr = {1}  WHERE ID = @Ti1ID;" + Environment.NewLine + Environment.NewLine, RTUID, _destination.IOAPrefixMultiplied);


            _sqlQueryTB.Text = sqlQuery;
            
        }


    }
}
