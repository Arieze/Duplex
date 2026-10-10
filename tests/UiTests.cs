using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace Duplex {
 public static class UiTests {
  static int passed;
  static void Assert(bool value,string message) {if(!value)throw new Exception("UI TEST FAILED: "+message);passed++;}
  static T Find<T>(MainUI ui,string name) where T:class {return ui.Window.FindName(name) as T;}
  static void Click(MainUI ui,string name) {Find<Button>(ui,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
  public static int Run(string root) {
   Directory.CreateDirectory(root);
   var ui=new MainUI(true);
   Assert(!Find<Button>(ui,"SyncButton").IsEnabled,"synchronization disabled before comparison");
   ui.Preview(Path.Combine(root,"interface.png"));
   var grid=Find<DataGrid>(ui,"FilesGrid");
   Assert(grid.Items.Count==6,"XAML and data bindings load");
   Click(ui,"SelectNoneButton");
   Assert(!Find<Button>(ui,"SyncButton").IsEnabled,"deselect all disables synchronization");
   Click(ui,"SelectAllButton");
   Assert(grid.Items.Cast<Entry>().Count(e=>e.Selected)==3,"unresolved conflicts stay excluded");
   Find<TextBox>(ui,"SearchBox").Text="rapport";
   Assert(grid.Items.Count==1,"search filters relative paths");
   Find<TextBox>(ui,"SearchBox").Text="";
   Find<ComboBox>(ui,"ModeBox").SelectedIndex=0;
   Assert(grid.Items.Cast<Entry>().Count(e=>e.Selected)==5,"mode switch updates planned actions");
   string a=Find<TextBox>(ui,"PathA").Text,b=Find<TextBox>(ui,"PathB").Text;
   Click(ui,"SwapButton");
   Assert(Find<TextBox>(ui,"PathA").Text==b&&Find<TextBox>(ui,"PathB").Text==a,"swap exchanges roots");
   Assert(grid.Items.Count==0&&!Find<Button>(ui,"SyncButton").IsEnabled,"path changes invalidate old comparison");
   Assert(Find<Border>(ui,"EmptyPanel").Visibility==Visibility.Visible,"empty state restored");
   File.WriteAllText(Path.Combine(root,"interface.txt"),"PASS: "+passed+" UI checks.\r\n");
   ui.Window.Close();return 0;
  }
 }
}
