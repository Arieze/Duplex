using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
namespace Duplex {
 public static class Tests {
  static int passed;
  static void Assert(bool condition,string message){if(!condition)throw new Exception("TEST FAILED: "+message);passed++;}
  static string Write(string dir,string name,string text){string p=Path.Combine(dir,name);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,text);return p;}
  static Comparison Compare(string a,string b,int mode){return Engine.Compare(a,b,true,true,mode,CancellationToken.None,s=>{});}
  static Outcome RunSync(Comparison c){return Engine.Synchronize(c,c.Entries.Where(x=>x.Selected&&x.Action!=Engine.Ignore).ToList(),CancellationToken.None,s=>{});}
  public static int Run(string root) {
   Directory.CreateDirectory(root);string run=Path.Combine(root,"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
   string a=Path.Combine(run,"A"),b=Path.Combine(run,"B");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
   Write(a,"nouveau.txt","nouveau");Write(a,@"sous-dossier\été 🎵.txt","accentué");Write(b,"à garder.txt","uniquement B");
   string sa=Write(a,"identique.txt","identique"),sb=Write(b,"identique.txt","identique");
   string da=Write(a,"conflit.txt","AAAA"),db=Write(b,"conflit.txt","BBBB");DateTime stamp=new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc);File.SetLastWriteTimeUtc(da,stamp);File.SetLastWriteTimeUtc(db,stamp);
   Comparison c=Compare(a,b,0);
   var repeatedResult=new Comparison();
   var repeated=Engine.Scan(a,true,repeatedResult,CancellationToken.None,s=>{},p=>Directory.EnumerateFileSystemEntries(p).SelectMany(x=>new[]{x,x}));
   Assert(repeatedResult.Errors.Count==0&&repeated.Count==4,"repeated file and folder enumeration does not duplicate inventory or block sync");
   Assert(repeated.ContainsKey(@"sous-dossier\été 🎵.txt"),"repeated directories retain nested files");
   var aliasResult=new Comparison();
   var aliases=Engine.Scan(a,true,aliasResult,CancellationToken.None,s=>{},p=>Directory.EnumerateFileSystemEntries(p).SelectMany(x=>new[]{x,Path.Combine(Path.GetDirectoryName(x),Path.GetFileName(x).ToUpperInvariant())}));
   Assert(aliasResult.Errors.Count==0&&aliases.Count==4,"case aliases confirmed by Windows file IDs are scanned once");
   Assert(c.Errors.Count==0,"scan clean");Assert(c.Entries.Count==5,"all paths union");Assert(c.Entries.Single(x=>x.Relative=="conflit.txt").Same==false,"same size and date different bytes");Assert(c.Entries.Single(x=>x.Relative=="identique.txt").Same,"same bytes, ignore dates");
   Outcome o=RunSync(c);Assert(o.Done==3&&o.Failed==0,"one way update count");Assert(File.ReadAllText(db)=="AAAA","new data committed");Assert(File.Exists(Path.Combine(b,"à garder.txt")),"update preserves destination extras");Assert(File.ReadAllText(Path.Combine(b,@"sous-dossier\été 🎵.txt"))=="accentué","unicode nested paths");
   Assert(Directory.GetFiles(Path.Combine(b,".duplex-backups"),"conflit.txt",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)=="BBBB"),"previous version backed up");
   c=Compare(a,b,3);Assert(c.Entries.Count==5,"backup area excluded");o=RunSync(c);Assert(o.Done==1&&o.Failed==0,"mirror only missing extra");Assert(!File.Exists(Path.Combine(b,"à garder.txt")),"mirror extra removed from active root");Assert(Directory.GetFiles(Path.Combine(b,".duplex-backups"),"à garder.txt",SearchOption.AllDirectories).Length==1,"mirror extra recoverable");
   Write(a,"gauche.txt","left");Write(b,"droite.txt","right");Write(a,"conflit2.txt","version A");Write(b,"conflit2.txt","version B");
   c=Compare(a,b,2);Entry conflict=c.Entries.Single(x=>x.Relative=="conflit2.txt");Assert(!conflict.Selected&&conflict.Action==Engine.Ignore,"fusion does not guess conflicts");o=RunSync(c);Assert(o.Done==2&&o.Failed==0,"fusion adds both ways");Assert(File.ReadAllText(Path.Combine(a,"droite.txt"))=="right","B to A copy");Assert(File.ReadAllText(Path.Combine(b,"conflit2.txt"))=="version B","unresolved conflict unchanged");
   c=Compare(a,b,0);Write(a,"conflit2.txt","changed after comparison and different size");o=RunSync(c);Assert(o.Failed==1&&File.ReadAllText(Path.Combine(b,"conflit2.txt"))=="version B","stale source rejected");
   c=Compare(a,b,0);Write(b,"conflit2.txt","destination modified meanwhile");o=RunSync(c);Assert(o.Failed==1&&File.ReadAllText(Path.Combine(b,"conflit2.txt"))=="destination modified meanwhile","stale destination rejected");
   c=Compare(a,b,0);var cancel=new CancellationTokenSource();cancel.Cancel();o=Engine.Synchronize(c,c.Entries.Where(x=>x.Selected).ToList(),cancel.Token,s=>{});Assert(o.Cancelled&&o.Done==0,"cancellation preserves all files");
   bool rejected=false;try{Engine.ValidateRoots(a,Path.Combine(a,"child"));}catch(IOException){rejected=true;}Assert(rejected,"nested roots rejected");
   rejected=false;try{Engine.Target(a,@"..\outside.txt");}catch(IOException){rejected=true;}Assert(rejected,"path traversal blocked");
   string testLeft=Path.Combine(run,"X"),testRight=Path.Combine(run,"Y");Directory.CreateDirectory(testLeft);Directory.CreateDirectory(testRight);Write(testLeft,"collision","file");Directory.CreateDirectory(Path.Combine(testRight,"collision"));Write(testRight,@"collision\keep.txt","protected");c=Compare(testLeft,testRight,0);o=RunSync(c);Assert(o.Failed==1&&File.Exists(Path.Combine(testRight,@"collision\keep.txt")),"file versus directory never overwrites tree");
   testLeft=Path.Combine(run,"LateA");testRight=Path.Combine(run,"LateB");Directory.CreateDirectory(testLeft);Directory.CreateDirectory(testRight);Write(testLeft,"new.txt","source");c=Compare(testLeft,testRight,0);Write(testRight,"new.txt","created by another app");o=RunSync(c);Assert(o.Failed==1&&File.ReadAllText(Path.Combine(testRight,"new.txt"))=="created by another app","new destination race blocked");
   string secret=Write(testLeft,"secret.txt","source cachée");File.SetAttributes(secret,File.GetAttributes(secret)|FileAttributes.Hidden);Write(testRight,"secret.txt","visible à conserver");
   c=Engine.Compare(testLeft,testRight,true,false,3,CancellationToken.None,s=>{});Assert(!c.Entries.Any(e=>e.Relative=="secret.txt"),"hidden exclusions apply on both sides, even in mirror");
   Write(testRight,"new.txt","target");
   using(var locked=new FileStream(Path.Combine(testLeft,"new.txt"),FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
    c=Compare(testLeft,testRight,0);Assert(c.Errors.Count>0,"locked common file blocks incomplete comparison");
    bool blocked=false;try{RunSync(c);}catch(IOException){blocked=true;}Assert(blocked,"incomplete inventory cannot synchronize");
   }
   Assert(Directory.GetFiles(run,".duplex-tmp-*",SearchOption.AllDirectories).Length==0,"no leftover staging files");
   string report="PASS: "+passed+" checks.\r\nIsolated test directory: "+run+"\r\n";
   File.WriteAllText(Path.Combine(root,"resultats.txt"),report);return 0;
  }
 }
}

