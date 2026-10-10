using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Forms=System.Windows.Forms;
[assembly: AssemblyTitle("Duplex")]
[assembly: AssemblyProduct("Duplex")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8")]
namespace Duplex {
 public static class Program {
  [STAThread] public static int Main(string[] args) {
   try {
    if(args.Length>0 && args[0]=="--self-test")return Tests.Run(args.Length>1?args[1]:Path.Combine(Path.GetTempPath(),"DuplexTests-"+Guid.NewGuid().ToString("N")));
    var app=new Application();app.ShutdownMode=ShutdownMode.OnMainWindowClose;
    if(args.Length>0 && args[0]=="--ui-test")return UiTests.Run(args[1]);
    app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(e.Exception.Message,"Duplex — Erreur",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};
    var ui=new MainUI(args.Length>0 && args[0]=="--preview");
    if(args.Length>0 && args[0]=="--preview") {ui.Preview(args[1]);return 0;}
    app.Run(ui.Window);return 0;
   }catch(Exception e) {
    if(args.Length>0){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostic.txt"),e.ToString());return 1;}
    MessageBox.Show(e.Message,"Duplex — Démarrage impossible",MessageBoxButton.OK,MessageBoxImage.Error);return 1;
   }
  }
 }
 public sealed class MainUI {
  public Window Window;
  TextBox pathA,pathB,search;ComboBox mode;CheckBox exact,hidden,onlyDiff;
  DataGrid grid;Button compare,sync,cancel;TextBlock status,sub,detail;ProgressBar progress;
  ObservableCollection<Entry> entries=new ObservableCollection<Entry>();Comparison comparison;CancellationTokenSource cancellation;bool busy,suppress;
  List<string> backupFolders=new List<string>();
  string SettingsPath {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Duplex","preferences.xml");}}
  T Find<T>(string name) where T:class {return Window.FindName(name) as T;}
  public MainUI(bool preview) {
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Duplex.MainWindow.xaml"))Window=(Window)XamlReader.Load(stream);
   using(var icon=Assembly.GetExecutingAssembly().GetManifestResourceStream("Duplex.icon.ico"))if(icon!=null)Window.Icon=BitmapFrame.Create(icon);
   pathA=Find<TextBox>("PathA");pathB=Find<TextBox>("PathB");search=Find<TextBox>("SearchBox");mode=Find<ComboBox>("ModeBox");
   exact=Find<CheckBox>("HashOption");hidden=Find<CheckBox>("HiddenOption");onlyDiff=Find<CheckBox>("DifferencesOnly");grid=Find<DataGrid>("FilesGrid");
   compare=Find<Button>("CompareButton");sync=Find<Button>("SyncButton");cancel=Find<Button>("CancelButton");status=Find<TextBlock>("StatusText");sub=Find<TextBlock>("SubStatus");detail=Find<TextBlock>("DetailText");progress=Find<ProgressBar>("Progress");
   grid.ItemsSource=entries;CollectionViewSource.GetDefaultView(entries).Filter=Filter;
   grid.SizeChanged+=(s,e)=>{if(grid.ActualWidth>500)grid.Columns[1].Width=new DataGridLength(Math.Max(220,grid.ActualWidth-500));};
   compare.Click+=async(s,e)=>await Compare();sync.Click+=async(s,e)=>await Sync();cancel.Click+=(s,e)=>{if(cancellation!=null)cancellation.Cancel();status.Text="Annulation demandée…";};
   Find<Button>("BrowseA").Click+=(s,e)=>Browse(pathA);Find<Button>("BrowseB").Click+=(s,e)=>Browse(pathB);
   Find<Button>("SwapButton").Click+=(s,e)=>{string t=pathA.Text;pathA.Text=pathB.Text;pathB.Text=t;};
   Find<Button>("SelectAllButton").Click+=(s,e)=>{foreach(var row in entries)row.Selected=row.Actionable && row.Action!=Engine.Ignore;UpdateSelection();};
   Find<Button>("SelectNoneButton").Click+=(s,e)=>{foreach(var row in entries)row.Selected=false;UpdateSelection();};
   Find<Button>("HelpButton").Click+=(s,e)=>Help();Find<Button>("HistoryButton").Click+=(s,e)=>Backups();
   pathA.TextChanged+=(s,e)=>Invalidate();pathB.TextChanged+=(s,e)=>Invalidate();exact.Click+=(s,e)=>Invalidate();hidden.Click+=(s,e)=>Invalidate();
   mode.SelectionChanged+=(s,e)=>{ModeHint();if(comparison!=null&&!busy){foreach(var row in entries)Engine.Configure(row,mode.SelectedIndex);grid.Items.Refresh();UpdateSelection();}};
   onlyDiff.Click+=(s,e)=>RefreshFilter();search.TextChanged+=(s,e)=>RefreshFilter();
   grid.SelectionChanged+=(s,e)=>{var row=grid.SelectedItem as Entry;if(row!=null){detail.Text=row.Relative+"  ·  A : "+Date(row.A)+"  ·  B : "+Date(row.B);detail.ToolTip=(row.A==null?"A : absent":"A : "+row.A.Full)+"\n"+(row.B==null?"B : absent":"B : "+row.B.Full);}};
   foreach(var box in new[]{pathA,pathB}) {
    TextBox target=box;
    target.PreviewDragOver+=(s,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
    target.PreviewDrop+=(s,e)=>{if(busy)return;var files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files!=null&&files.Length==1&&Directory.Exists(files[0]))target.Text=files[0];e.Handled=true;};
   }
   Window.Closing+=(s,e)=>{if(busy){e.Cancel=true;cancellation.Cancel();status.Text="Arrêt en cours. Vous pourrez ensuite fermer Duplex.";}};
   if(!preview)LoadSettings();ModeHint();
  }
  string Date(Stamp s){return s==null?"absent":new DateTime(s.Modified,DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");}
  void ModeHint() {
   string[] hints={"Met à jour B, sans supprimer ses fichiers propres.","Met à jour A, sans supprimer ses fichiers propres.","Fusionne les ajouts ; conflits à choisir.","Aligne B sur A ; fichiers en trop archivés."};
   Find<TextBlock>("ModeHint").Text=hints[Math.Max(0,mode.SelectedIndex)];
  }
  bool Filter(object item) {var e=item as Entry;if(e==null)return false;return (!(onlyDiff.IsChecked==true)||!e.Same)&&(String.IsNullOrEmpty(search.Text)||e.Relative.IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0);}
  void RefreshFilter(){var v=CollectionViewSource.GetDefaultView(entries);if(!v.IsEditingItemSafe())v.Refresh();}
  void Browse(TextBox box) {
   using(var dialog=new Forms.FolderBrowserDialog()){dialog.Description="Choisir un dossier à comparer";dialog.ShowNewFolderButton=true;if(Directory.Exists(box.Text))dialog.SelectedPath=box.Text;if(dialog.ShowDialog()==Forms.DialogResult.OK)box.Text=dialog.SelectedPath;}
  }
  void Invalidate() {
   if(suppress||busy)return;comparison=null;entries.Clear();sync.IsEnabled=false;Find<Border>("EmptyPanel").Visibility=Visibility.Visible;
   foreach(string n in new[]{"CountA","CountB","CountDiff","CountSame"})Find<TextBlock>(n).Text="—";
   status.Text="Prêt à comparer";sub.Text="";detail.Text="";
  }
  void SetBusy(bool value) {
   busy=value;foreach(string n in new[]{"PathA","PathB","BrowseA","BrowseB","SwapButton","ModeBox","HashOption","HiddenOption","SelectAllButton","SelectNoneButton","FilesGrid"})Find<UIElement>(n).IsEnabled=!value;
   compare.IsEnabled=!value;sync.IsEnabled=false;cancel.Visibility=value?Visibility.Visible:Visibility.Collapsed;progress.Visibility=value?Visibility.Visible:Visibility.Collapsed;progress.IsIndeterminate=value;
  }
  Action<string> Reporter() {
   long last=0;return message=>{long now=DateTime.UtcNow.Ticks;if(now-last<TimeSpan.TicksPerMillisecond*90)return;last=now;Window.Dispatcher.BeginInvoke(new Action(()=>status.Text=message));};
  }
  async Task Compare() {
   if(busy)return;
   string a,b;try{a=Engine.Root(pathA.Text);b=Engine.Root(pathB.Text);Engine.ValidateRoots(a,b);}catch(Exception e){MessageBox.Show(Window,e.Message,"Dossiers à vérifier",MessageBoxButton.OK,MessageBoxImage.Information);return;}
   comparison=null;entries.Clear();SetBusy(true);cancellation=new CancellationTokenSource();bool sha=exact.IsChecked==true;bool includeHidden=hidden.IsChecked==true;int selectedMode=mode.SelectedIndex;var report=Reporter();status.Text="Préparation de la comparaison…";sub.Text=sha?"Comparaison du contenu":"Comparaison rapide : taille et date";
   try {
    var result=await Task.Run(()=>Engine.Compare(a,b,sha,includeHidden,selectedMode,cancellation.Token,report));comparison=result;
    foreach(var entry in result.Entries){entry.PropertyChanged+=(s,e)=>{if(e.PropertyName=="Selected")UpdateSelection();};entries.Add(entry);}
    Find<TextBlock>("CountA").Text=entries.Count(x=>x.A!=null&&x.B==null).ToString("N0");Find<TextBlock>("CountB").Text=entries.Count(x=>x.B!=null&&x.A==null).ToString("N0");Find<TextBlock>("CountDiff").Text=entries.Count(x=>x.A!=null&&x.B!=null&&!x.Same).ToString("N0");Find<TextBlock>("CountSame").Text=entries.Count(x=>x.Same).ToString("N0");
    Find<TextBlock>("SameLabel").Text=sha?"Fichiers identiques":"Présumés identiques";
    Find<Border>("EmptyPanel").Visibility=Visibility.Collapsed;status.Text=result.Entries.Count.ToString("N0")+" fichiers comparés";
    detail.Text="Analyse terminée · "+result.Skipped.Count+" liens exclus · "+result.Errors.Count+" erreurs.";
    if(result.Errors.Count>0)MessageBox.Show(Window,"La synchronisation est bloquée car certains fichiers n’ont pas pu être analysés.\n\n"+String.Join("\n",result.Errors.Take(10)),"Comparaison incomplète",MessageBoxButton.OK,MessageBoxImage.Warning);
    SaveSettings();
   }catch(OperationCanceledException){status.Text="Comparaison annulée";sub.Text="Aucun fichier modifié.";}catch(Exception ex){status.Text="Comparaison interrompue";MessageBox.Show(Window,ex.Message,"Erreur de comparaison",MessageBoxButton.OK,MessageBoxImage.Error);}
   finally{SetBusy(false);if(comparison!=null)UpdateSelection();}
  }
  void UpdateSelection() {
   if(busy||comparison==null)return;
   var chosen=entries.Where(x=>x.Selected&&x.Action!=Engine.Ignore).ToList();long size=chosen.Sum(x=>x.Action==Engine.ToB?x.A.Size:x.Action==Engine.ToA?x.B.Size:0);
   int conflicts=entries.Count(x=>x.A!=null&&x.B!=null&&!x.Same&&x.Action==Engine.Ignore);
   sub.Text=chosen.Count+" opérations sélectionnées · "+Engine.Size(size)+" à copier"+(conflicts>0?" · "+conflicts+" conflits ignorés":"")+(comparison.Errors.Count>0?" · Analyse incomplète, synchronisation bloquée":"");
   sync.IsEnabled=chosen.Count>0&&comparison.Errors.Count==0;sync.Content=chosen.Count>0?"Synchroniser "+chosen.Count+" fichiers  →":"Synchroniser";
  }
  async Task Sync() {
   if(busy||comparison==null)return;grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);
   var chosen=entries.Where(x=>x.Selected&&x.Action!=Engine.Ignore).ToList();if(chosen.Count==0)return;
   int archives=chosen.Count(x=>x.Action==Engine.Archive);int overwrite=chosen.Count(x=>(x.Action==Engine.ToB&&x.B!=null)||(x.Action==Engine.ToA&&x.A!=null));
   string message=chosen.Count+" opérations vont être effectuées.\n\n"+chosen.Count(x=>x.Action==Engine.ToB)+" copies de A vers B\n"+chosen.Count(x=>x.Action==Engine.ToA)+" copies de B vers A\n"+overwrite+" fichiers existants remplacés (et sauvegardés)\n"+archives+" fichiers retirés de B (et archivés)\n\nA : "+comparison.RootA+"\nB : "+comparison.RootB+"\n\nLes sauvegardes se trouvent dans .duplex-backups dans le dossier concerné.\nContinuer ?";
   if(MessageBox.Show(Window,message,"Vérifier la synchronisation",MessageBoxButton.YesNo,archives>0?MessageBoxImage.Warning:MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
   SetBusy(true);cancellation=new CancellationTokenSource();var snapshot=comparison;var report=Reporter();
   try {
    Outcome result=await Task.Run(()=>Engine.Synchronize(snapshot,chosen,cancellation.Token,report));backupFolders=result.BackupFolders;
    status.Text=(result.Cancelled?"Synchronisation interrompue":"Synchronisation terminée")+" · "+result.Done+" fichiers traités";
    sub.Text=result.Failed>0?result.Failed+" échecs. Les autres fichiers ont été traités. Relancez la comparaison.":"Relancez la comparaison pour actualiser la liste.";
    if(result.Errors.Count>0)MessageBox.Show(Window,String.Join("\n",result.Errors.Take(12)),"Fichiers non synchronisés",MessageBoxButton.OK,MessageBoxImage.Warning);
   }catch(Exception ex){status.Text="Synchronisation interrompue";sub.Text="Relancez une comparaison avant de poursuivre.";MessageBox.Show(Window,ex.Message,"Erreur",MessageBoxButton.OK,MessageBoxImage.Error);}
   finally{comparison=null;SetBusy(false);grid.IsEnabled=false;sync.IsEnabled=false;}
  }
  void Backups() {
   var paths=new List<string>(backupFolders.Where(Directory.Exists));
   if(paths.Count==0)foreach(string p in new[]{pathB.Text,pathA.Text})try{string b=Path.Combine(p,".duplex-backups");if(Directory.Exists(b))paths.Add(b);}catch{}
   if(paths.Count==0){MessageBox.Show(Window,"Les fichiers remplacés ou retirés seront rangés dans .duplex-backups, à la racine du dossier concerné.\n\nPour récupérer un fichier, copiez-le depuis la session datée vers son emplacement d’origine. Le journal décrit les opérations.","Vos sauvegardes",MessageBoxButton.OK,MessageBoxImage.Information);return;}
   foreach(string p in paths.Distinct())Process.Start(new ProcessStartInfo("explorer.exe","\""+p+"\"") {UseShellExecute=true});
  }
  void Help() {MessageBox.Show(Window,"1. Choisissez deux dossiers ou déposez-les dans les champs A et B.\n2. Choisissez un mode, puis cliquez sur Comparer.\n3. Vérifiez les lignes cochées et l’action de chaque ligne.\n4. Cliquez sur Synchroniser et confirmez le récapitulatif.\n\nA → B / B → A : copie et mise à jour, sans retirer les fichiers propres à l’autre côté.\nFusion : copie les fichiers absents de chaque côté. Les versions différentes sont ignorées jusqu’à votre choix.\nMiroir : aligne les fichiers de B sur A. Les fichiers retirés vont en sauvegarde. Les dossiers vides restent hors comparaison.\n\nLa vérification SHA-256 compare le contenu ; le mode rapide se fie à la taille et à la date. Les fichiers en cours d’écriture peuvent être refusés. Les liens symboliques et jonctions ne sont jamais suivis.\n\nLes sauvegardes sont locales et ne sont jamais purgées automatiquement. Duplex ne reproduit pas les droits NTFS ni les flux alternatifs. Une interruption conserve les opérations déjà terminées.\n\nDuplex 1.1.0 · Windows · Aucune connexion réseau de l’application.","Mode d’emploi",MessageBoxButton.OK,MessageBoxImage.Information);}
  void LoadSettings() {try{if(!File.Exists(SettingsPath))return;var x=XDocument.Load(SettingsPath).Root;pathA.Text=(string)x.Element("A")??"";pathB.Text=(string)x.Element("B")??"";int m;if(Int32.TryParse((string)x.Element("Mode"),out m)&&m>=0&&m<=3)mode.SelectedIndex=m;}catch{}}
  void SaveSettings() {try{Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));new XDocument(new XElement("Duplex",new XElement("A",pathA.Text),new XElement("B",pathB.Text),new XElement("Mode",mode.SelectedIndex))).Save(SettingsPath);}catch{}}
  public void Preview(string filename) {
   suppress=true;pathA.Text=@"C:\Dossiers\Source";pathB.Text=@"D:\Dossiers\Copie";mode.SelectedIndex=2;suppress=false;
   comparison=new Comparison {RootA=pathA.Text,RootB=pathB.Text};
   string[] names={@"Documents\rapport.pdf",@"Photos\vacances.jpg",@"Projets\presentation.pptx",@"Musique\piste.flac",@"Archives\notes.txt",@"Videos\montage.mp4"};
   for(int i=0;i<names.Length;i++) {var e=new Entry {Relative=names[i],A=i==1?null:new Stamp{Size=27500000+i*331241,Modified=DateTime.UtcNow.Ticks},B=i==0||i==3?null:new Stamp{Size=28500000+i*123450,Modified=DateTime.UtcNow.Ticks},Status=i==0||i==3?"Uniquement A":i==1?"Uniquement B":"Différent"};Engine.Configure(e,2);entries.Add(e);}
   Find<TextBlock>("CountA").Text="2";Find<TextBlock>("CountB").Text="1";Find<TextBlock>("CountDiff").Text="3";Find<TextBlock>("CountSame").Text="0";Find<Border>("EmptyPanel").Visibility=Visibility.Collapsed;
   status.Text="6 fichiers comparés";UpdateSelection();
   detail.Text="3 conflits à examiner";
   var content=(FrameworkElement)Window.Content;content.Measure(new Size(1320,880));content.Arrange(new Rect(0,0,1320,880));content.UpdateLayout();
   var bitmap=new RenderTargetBitmap(1320,880,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(filename))encoder.Save(f);
  }
 }
 public static class ViewHelper {public static bool IsEditingItemSafe(this ICollectionView view){var editable=view as IEditableCollectionView;return editable!=null&&(editable.IsEditingItem||editable.IsAddingNew);}}
}


