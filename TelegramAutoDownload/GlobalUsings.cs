// Disambiguate WPF types from Windows Forms types (both are referenced since we use NotifyIcon)
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Button = System.Windows.Controls.Button;
global using CheckBox = System.Windows.Controls.CheckBox;
global using ComboBox = System.Windows.Controls.ComboBox;
global using ListViewItem = System.Windows.Controls.ListViewItem;
global using TextBox = System.Windows.Controls.TextBox;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
global using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;
