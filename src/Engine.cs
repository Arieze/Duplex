using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Duplex {
 public sealed class Stamp {
  public string Full, Relative, Hash;
  public long Size, Modified;
  public static Stamp Read(string full, string relative) {
   FileInfo f = new FileInfo(full);
   return new Stamp { Full=full, Relative=relative, Size=f.Length, Modified=f.LastWriteTimeUtc.Ticks };
  }
 }
 public sealed class Entry : INotifyPropertyChanged {
  public string Relative {get;set;}
  public Stamp A, B;
  public bool Same;
  public string Status {get;set;}
  public string Sizes {get {return Engine.Size(A==null ? -1:A.Size)+" / "+Engine.Size(B==null ? -1:B.Size);}}
  public List<string> Options {get;set;}
  bool selected; string action;
  public bool Actionable {get {return !Same;}}
  public bool Selected {get {return selected;} set {selected=value && Actionable; Changed("Selected");}}
  public string Action {get {return action;} set {action=value; selected=Actionable && action!=Engine.Ignore; Changed("Action"); Changed("Selected");}}
  public event PropertyChangedEventHandler PropertyChanged;
  void Changed(string name) {if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs(name));}
 }
 public sealed class Comparison {
  public string RootA, RootB;
  public List<Entry> Entries=new List<Entry>();
  public List<string> Errors=new List<string>();
  public List<string> Skipped=new List<string>();
  public HashSet<string> Excluded=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 }
 public sealed class Outcome {
  public int Done, Failed;
  public bool Cancelled;
  public List<string> Errors=new List<string>();
  public List<string> BackupFolders=new List<string>();
 }
 public static class Engine {
  [StructLayout(LayoutKind.Sequential)]
  struct FileIdentity {
   public uint Attributes;
   public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;
   public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
  }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)]
  static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileIdentity info);
  static bool SameObject(string first,string second) {
   // Only collapse case aliases when Windows confirms the same volume/file ID.
   using(var a=CreateFile(first,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero))
   using(var b=CreateFile(second,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)) {
    FileIdentity x,y;
    return !a.IsInvalid&&!b.IsInvalid&&GetFileInformationByHandle(a,out x)&&GetFileInformationByHandle(b,out y)
     &&(x.IndexHigh!=0||x.IndexLow!=0)&&x.Volume==y.Volume&&x.IndexHigh==y.IndexHigh&&x.IndexLow==y.IndexLow;
   }
  }
  public const string Ignore="Ignorer", ToB="Copier A → B", ToA="Copier B → A", Archive="Archiver dans B";
  public static string Size(long bytes) {
   if(bytes<0)return "—";
   string[] units={"o","Ko","Mo","Go","To"};double v=bytes;int i=0;
   while(v>=1024 && i<4){v/=1024;i++;}return v.ToString(i==0?"0":"0.#")+" "+units[i];
  }
  public static string Root(string value) {
   if(String.IsNullOrWhiteSpace(value))throw new IOException("Choisissez les deux dossiers avant de continuer.");
   string p=Path.GetFullPath(value.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);
   if(p.Length==2 && p[1]==':')p+="\\";
   if(!Directory.Exists(p))throw new IOException("Dossier introuvable : "+p);
   CheckLinks(p);
   return p;
  }
  public static bool Under(string child,string root) {return child.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
  public static void ValidateRoots(string a,string b) {
   if(String.Equals(a,b,StringComparison.OrdinalIgnoreCase)||Under(a,b)||Under(b,a))throw new IOException("Choisissez deux dossiers distincts : l’un ne doit pas se trouver dans l’autre.");
  }
  public static void CheckLinks(string path) {
   string current=Path.GetFullPath(path);
   while(!String.IsNullOrEmpty(current)) {
    if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Lien symbolique ou jonction refusé : "+current);
    DirectoryInfo parent=Directory.GetParent(current);current=parent==null?null:parent.FullName;
   }
  }
  public static string Target(string root,string relative) {
   if(Path.IsPathRooted(relative))throw new IOException("Chemin relatif invalide.");
   string p=Path.GetFullPath(Path.Combine(root,relative));
   if(!Under(p,root))throw new IOException("Chemin situé hors du dossier choisi.");
   CheckLinks(p);return p;
  }
  internal static Dictionary<string,Stamp> Scan(string root,bool hidden,Comparison result,CancellationToken token,Action<string> status,Func<string,IEnumerable<string>> enumerate=null) {
   var files=new Dictionary<string,Stamp>(StringComparer.OrdinalIgnoreCase);var stack=new Stack<string>();stack.Push(root);int count=0;
   var seen=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   if(enumerate==null)enumerate=Directory.EnumerateFileSystemEntries;
   while(stack.Count>0) {
    token.ThrowIfCancellationRequested();string folder=stack.Pop();
    try {
     foreach(string p in enumerate(folder)) {
      token.ThrowIfCancellationRequested();string name=Path.GetFileName(p);
      if(name.Equals(".duplex-backups",StringComparison.OrdinalIgnoreCase)||name.StartsWith(".duplex-tmp-",StringComparison.OrdinalIgnoreCase))continue;
      try {
       string full=Path.GetFullPath(p),previous;
       if(seen.TryGetValue(full,out previous)) {
        if(String.Equals(previous,full,StringComparison.Ordinal)||SameObject(previous,full))continue;
        throw new IOException("Deux chemins distincts ne diffèrent que par les majuscules/minuscules. Renommez l’un des deux dossiers ou fichiers avant de comparer : "+previous+" et "+full);
       }
       seen.Add(full,full);
       FileAttributes attr=File.GetAttributes(p);
       string relative=p.Substring(root.TrimEnd('\\').Length+1);
       if((attr&FileAttributes.ReparsePoint)!=0){result.Skipped.Add(p);result.Excluded.Add(relative);continue;}
       if(!hidden && (attr&FileAttributes.Hidden)!=0){result.Excluded.Add(relative);continue;}
       if((attr&FileAttributes.Directory)!=0)stack.Push(p);
       else {string rel=p.Substring(root.TrimEnd('\\').Length+1);Stamp existing;if(files.TryGetValue(rel,out existing))throw new IOException("Deux chemins diffèrent uniquement par les majuscules/minuscules : "+existing.Full+" et "+p);files.Add(rel,Stamp.Read(p,rel));count++;if(count%100==0)status("Inventaire · "+count+" fichiers dans "+root);}
      }catch(OperationCanceledException){throw;}catch(Exception e){result.Errors.Add(p+" : "+e.Message);}
     }
    }catch(OperationCanceledException){throw;}catch(Exception e){result.Errors.Add(folder+" : "+e.Message);}
   }return files;
  }
  public static string Hash(string path,CancellationToken token) {
   using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan))return HashStream(f,token);
  }
  static string HashStream(Stream stream,CancellationToken token) {
   using(var sha=SHA256.Create()) {byte[] buffer=new byte[1024*1024];int n;
    while((n=stream.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();sha.TransformBlock(buffer,0,n,buffer,0);}
    sha.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(sha.Hash).Replace("-","");
   }
  }
  public static Comparison Compare(string a,string b,bool exact,bool hidden,int mode,CancellationToken token,Action<string> status) {
   a=Root(a);b=Root(b);ValidateRoots(a,b);
   var result=new Comparison {RootA=a,RootB=b};status("Lecture du dossier A…");var left=Scan(a,hidden,result,token,status);
   status("Lecture du dossier B…");var right=Scan(b,hidden,result,token,status);
   var paths=left.Keys.Union(right.Keys,StringComparer.OrdinalIgnoreCase).Where(p=>!result.Excluded.Any(x=>p.Equals(x,StringComparison.OrdinalIgnoreCase)||p.StartsWith(x+"\\",StringComparison.OrdinalIgnoreCase))).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();int index=0;
   foreach(string rel in paths) {
    token.ThrowIfCancellationRequested();Stamp sa,sb;left.TryGetValue(rel,out sa);right.TryGetValue(rel,out sb);
    Entry entry=new Entry {Relative=rel,A=sa,B=sb};
    if(sa!=null && sb!=null) {
     try {
      if(exact && sa.Size==sb.Size){status("Comparaison du contenu · "+(++index)+" · "+rel);sa.Hash=Hash(sa.Full,token);sb.Hash=Hash(sb.Full,token);CheckStamp(sa);CheckStamp(sb);entry.Same=sa.Hash==sb.Hash;}
      else entry.Same=sa.Size==sb.Size && sa.Modified==sb.Modified;
      entry.Status=entry.Same?(exact?"Identique":"Taille/date identiques"):"Différent";
     }catch(OperationCanceledException){throw;}catch(Exception e){result.Errors.Add(rel+" : "+e.Message);entry.Status="Lecture impossible";}
    }else entry.Status=sa!=null?"Uniquement A":"Uniquement B";
    Configure(entry,mode);result.Entries.Add(entry);
   }return result;
  }
  public static void Configure(Entry e,int mode) {
   e.Options=new List<string>();
   if(!e.Same) {
    if(e.A!=null)e.Options.Add(ToB);
    if(e.B!=null)e.Options.Add(ToA);
    if(mode==3 && e.A==null && e.B!=null)e.Options.Add(Archive);
   }e.Options.Add(Ignore);
   if(e.Same)e.Action=Ignore;
   else if(mode==0)e.Action=e.A!=null?ToB:Ignore;
   else if(mode==1)e.Action=e.B!=null?ToA:Ignore;
   else if(mode==2)e.Action=e.A==null?ToA:e.B==null?ToB:Ignore;
   else e.Action=e.A!=null?ToB:Archive;
  }
  public static void CheckStamp(Stamp expected) {
   if(expected==null)throw new IOException("État de fichier absent.");
   CheckLinks(expected.Full);
   if(!File.Exists(expected.Full))throw new IOException("Le fichier a disparu depuis la comparaison : "+expected.Full);
   Stamp now=Stamp.Read(expected.Full,expected.Relative);
   if(now.Size!=expected.Size || now.Modified!=expected.Modified)throw new IOException("Le fichier a changé depuis la comparaison. Relancez l’analyse : "+expected.Relative);
  }
  static void CheckDestination(string path,Stamp expected,CancellationToken token) {
   CheckLinks(path);
   if(Directory.Exists(path))throw new IOException("Un dossier porte déjà le nom du fichier : "+path);
   if(expected==null){if(File.Exists(path))throw new IOException("Un fichier est apparu depuis la comparaison : "+path);}
   else {CheckStamp(expected);if(expected.Hash!=null && Hash(path,token)!=expected.Hash)throw new IOException("Le contenu de la destination a changé : "+expected.Relative);}
  }
  static string BackupPath(string root,string session,string rel,Outcome result) {
   string sessionRoot=Target(root,Path.Combine(".duplex-backups",session));
   if(!result.BackupFolders.Contains(sessionRoot))result.BackupFolders.Add(sessionRoot);
   string path=Target(root,Path.Combine(".duplex-backups",session,"fichiers",rel));Directory.CreateDirectory(Path.GetDirectoryName(path));return path;
  }
  static void Journal(string root,string session,string message,Outcome result) {
   string dir=Target(root,Path.Combine(".duplex-backups",session));Directory.CreateDirectory(dir);
   if(!result.BackupFolders.Contains(dir))result.BackupFolders.Add(dir);
   File.AppendAllText(Path.Combine(dir,"journal.txt"),DateTime.Now.ToString("HH:mm:ss")+"\t"+message+Environment.NewLine,Encoding.UTF8);
  }
  static void CopyOne(Stamp source,Stamp destination,string destRoot,string session,Outcome result,CancellationToken token) {
   CheckStamp(source);string target=Target(destRoot,source.Relative);CheckDestination(target,destination,token);
   string parent=Path.GetDirectoryName(target);Directory.CreateDirectory(parent);CheckLinks(parent);
   string temp=Path.Combine(parent,".duplex-tmp-"+Guid.NewGuid().ToString("N"));string sourceHash;
   try {
    using(var input=new FileStream(source.Full,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan)) {
     CheckStamp(source);
     using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,FileOptions.WriteThrough))
     using(var sha=SHA256.Create()) {
      byte[] buffer=new byte[1024*1024];int n;
      while((n=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,n);sha.TransformBlock(buffer,0,n,buffer,0);}
      sha.TransformFinalBlock(new byte[0],0,0);sourceHash=BitConverter.ToString(sha.Hash).Replace("-","");output.Flush(true);
     }
     CheckStamp(source);
     if(source.Hash!=null && sourceHash!=source.Hash)throw new IOException("Le contenu de la source a changé : "+source.Relative);
     if(Hash(temp,token)!=sourceHash)throw new IOException("La vérification de copie a échoué : "+source.Relative);
     token.ThrowIfCancellationRequested();CheckDestination(target,destination,token);
     File.SetLastWriteTimeUtc(temp,new DateTime(source.Modified,DateTimeKind.Utc));
     Journal(destRoot,session,"PRÉPARATION COPIE\t"+source.Relative,result);
     if(destination!=null) {
      string backup=BackupPath(destRoot,session,source.Relative,result);
      // Same-volume moves also work on exFAT. Restore the old file if commit fails.
      File.Move(target,backup);
      try {File.Move(temp,target);}catch {if(!File.Exists(target) && File.Exists(backup))File.Move(backup,target);throw;}
     } else File.Move(temp,target);
    }
    Journal(destRoot,session,"COPIE VALIDÉE\t"+source.Relative,result);
   }finally {if(File.Exists(temp)){try{File.Delete(temp);}catch{}}}
  }
  public static Outcome Synchronize(Comparison comparison,IList<Entry> chosen,CancellationToken token,Action<string> status) {
   if(comparison.Errors.Count>0)throw new IOException("L’analyse contient des erreurs. Corrigez-les puis comparez à nouveau.");
   Root(comparison.RootA);Root(comparison.RootB);ValidateRoots(comparison.RootA,comparison.RootB);
   var result=new Outcome();string session=DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")+"_"+Guid.NewGuid().ToString("N").Substring(0,6);
   foreach(Entry e in chosen) {
    try {
     token.ThrowIfCancellationRequested();status("Synchronisation · "+(result.Done+result.Failed+1)+"/"+chosen.Count+" · "+e.Relative);
     if(e.Action==ToB)CopyOne(e.A,e.B,comparison.RootB,session,result,token);
     else if(e.Action==ToA)CopyOne(e.B,e.A,comparison.RootA,session,result,token);
     else if(e.Action==Archive) {
      if(e.A!=null || e.B==null)throw new IOException("Archivage non valide.");
      string absent=Target(comparison.RootA,e.Relative);
      if(File.Exists(absent)||Directory.Exists(absent))throw new IOException("La source a changé ; archivage annulé : "+e.Relative);
      string target=Target(comparison.RootB,e.Relative);CheckDestination(target,e.B,token);token.ThrowIfCancellationRequested();
      Journal(comparison.RootB,session,"PRÉPARATION ARCHIVAGE\t"+e.Relative,result);
      File.Move(target,BackupPath(comparison.RootB,session,e.Relative,result));Journal(comparison.RootB,session,"ARCHIVÉ\t"+e.Relative,result);
     } else continue;
     result.Done++;
    }catch(OperationCanceledException){result.Cancelled=true;break;}catch(Exception ex){result.Failed++;result.Errors.Add(e.Relative+" : "+ex.Message);}
   }return result;
  }
 }
}
