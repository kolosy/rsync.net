/**
 *  Copyright (C) 2006 Alex Pedenko
 * 
 *  This program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program; if not, write to the Free Software
 *  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
 */
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace NetSync
{
	public class WinRsync
	{
		[STAThread]
		static void Main(string[] args)
		{
			MainClass mc = new MainClass();
			mc.Run(args);
		}
	}

	public class MainClass
	{
		const string BACKUP_SUFFIX  = "~";
		const string RSYNC_NAME = "rsync";
		const string RSYNC_VERSION = "1.0";
		public static Options opt;
		
		public void Run(string[] args)
		{
			
			opt	= new Options();
			opt.Init();
			if(args.Length  == 0)
			{
				Usage();				
				MainClass.Exit("", null);
			}
			int argsNotUsed = CommandLineParser.ParseArguments(args, opt);
			if (argsNotUsed == -1)
			{			
				MainClass.Exit("Error parsing options", null);
			}
			string[] args2 = new string[argsNotUsed];
			for(int i = 0; i < argsNotUsed; i++)
				args2[i] = args[args.Length - argsNotUsed + i];
			
			if(opt.amDaemon && !opt.amSender)
			{
				Daemon.DaemonMain(opt);							
				return;
			}	
			ClientInfo cInfo = new ClientInfo();
			cInfo.Options = opt;
			StartClient(args2, cInfo);
			opt.doStats = true;
			cInfo.IoStream = null;
			Report(cInfo);
			Console.Write("Press 'Enter' to exit.");
			Console.Read();
		}


		public static void Report(ClientInfo cInfo)
		{			
			IOStream f = cInfo.IoStream;
			Options options = cInfo.Options;
		
			Int64 totalWritten   = Options.stats.totalWritten;
			Int64 totalRead   = Options.stats.totalRead;
			if (options.amServer && f != null) 
			{
				if (options.amSender) 
				{
					f.WriteLongInt(totalRead);
					f.WriteLongInt(totalWritten);
					f.WriteLongInt(Options.stats.totalSize);
				}
				return;
			}
			if (!options.amSender && f != null) 
			{
				/* Read the first two in opposite order because the meaning of
				 * read/write swaps when switching from sender to receiver. */				
				totalWritten = f.ReadLongInt();
				totalRead = f.ReadLongInt();
				Options.stats.totalSize = f.ReadLongInt();
			}

			if (options.doStats) 
			{
				Log.WriteLine("Number of files: " + Options.stats.numFiles);
				Log.WriteLine("Number of files transferred: " + Options.stats.numTransferredFiles);
				Log.WriteLine("Total file size: " + Options.stats.totalSize);
				Log.WriteLine("Total transferred file size: " + Options.stats.totalTransferredSize);
				Log.WriteLine("Literal data: " + Options.stats.literalData);
				Log.WriteLine("Matched data: " + Options.stats.matchedData);
				Log.WriteLine("File list size: " + Options.stats.flistSize);
				Log.WriteLine("Total bytes written: " + totalWritten);
				Log.WriteLine("Total bytes received: " + totalRead);
			} 			
		}

		public static int StartClient(string[] args, ClientInfo cInfo)
		{			
			Options options = cInfo.Options;
			if (args[0].StartsWith(Options.URL_PREFIX) && !options.readBatch) //source is remote
			{
				string path, user = "";
				string host = args[0].Substring(Options.URL_PREFIX.Length,args[0].Length - Options.URL_PREFIX.Length);
				if(host.LastIndexOf('@') != -1)
				{				
					user = host.Substring(0, host.LastIndexOf('@'));
					host = host.Substring(host.LastIndexOf('@') + 1);
				} 
				else
				{											
					MainClass.Exit("Unknown host", null);
				}
				if(host.IndexOf("/") != -1)
				{
					path = host.Substring(host.IndexOf("/")+1);
					host = host.Substring(0, host.IndexOf("/"));
					
				} 
				else
					path ="";
				if(host[0] == '[' && host.IndexOf(']') != -1)
				{
					host = host.Remove(host.IndexOf(']'),1);
					host = host.Remove(host.IndexOf('['),1);
				}
				if(host.IndexOf(':') != -1)
				{
					options.rsyncPort = Convert.ToInt32(host.Substring(host.IndexOf(':')));
					host = host.Substring(0, host.IndexOf(':'));
				}
				string[] newArgs = (string[])Util.ArrayDeleteFirstElement(args);
				return StartSocketClient(host, path, user, newArgs, cInfo);
			}

			//source is local
			if(!options.readBatch)
			{
				int p = Util.FindColon(args[0]);
				string user = "";
				options.amSender = true;
				if (args[args.Length - 1].StartsWith(Options.URL_PREFIX) && !options.readBatch)
				{
					string path;
					string host = args[args.Length - 1].Substring(Options.URL_PREFIX.Length);
					if(host.LastIndexOf('@') != -1)
					{				
						user = host.Substring(0, host.LastIndexOf('@'));
						host = host.Substring(host.LastIndexOf('@') + 1);
					} 
					else
					{
						MainClass.Exit("Unknown host", null);
					}
					if(host.IndexOf("/") != -1)
					{
						path = host.Substring(host.IndexOf("/")+1);
						host = host.Substring(0, host.IndexOf("/"));
				
					} 
					else
						path ="";
					if(host[0] == '[' && host.IndexOf(']') != -1)
					{
						host = host.Remove(host.IndexOf(']'),1);
						host = host.Remove(host.IndexOf('['),1);
					}
					if(host.IndexOf(':') != -1)
					{
						options.rsyncPort = Convert.ToInt32(host.Substring(host.IndexOf(':')));
						host = host.Substring(0, host.IndexOf(':'));
					}
					string[] newArgs = (string[])Util.ArrayDeleteLastElement(args);
					return StartSocketClient(host, path, user, newArgs, cInfo);
				}
				p = Util.FindColon(args[args.Length - 1]);					
				if(p == -1) //src & dest are local
				{
					/* no realized*/
				} 
				else
					if (args[args.Length - 1][p + 1] == ':')
					{
						if(options.shellCmd == null)
                            return StartSocketClient(args[args.Length - 1].Substring(0, p), args[args.Length - 1].Substring(p + 2), user, args, cInfo);
					}
			}
			return 0;
		}

		public static int StartSocketClient(string host, string path, string user, string[] args, ClientInfo cInfo)
		{
			Options options = cInfo.Options;
			if(path.CompareTo("") != 0 && path[0] == '/')
			{
				Log.WriteLine("ERROR: The remote path must start with a module name not a /");
				return -1;
			}			
			cInfo.IoStream = OpenSocketOutWrapped(host, options.rsyncPort, options.bindAddress);

			if(cInfo.IoStream != null)
				StartInbandExchange(user,path, cInfo, args.Length);

			Client client = new Client();
			return client.ClientRun(cInfo, -1, args);
		}

		public static int StartInbandExchange(string user, string path, ClientInfo cInfo, int argc)
		{
			Options options = cInfo.Options;
			IOStream f = cInfo.IoStream;

			string[] sargs = new string[Options.MAX_ARGS];
			int sargc = options.ServerOptions(sargs);
			sargs[sargc++] = ".";
			//if(path != null && path.Length>0)
				//sargs[sargc++] = path;

			if(argc == 0 && !options.amSender)
				options.listOnly = true;
			if(path[0] == '/')
			{
				Log.WriteLine("ERROR: The remote path must start with a module name");
				return -1;
			}
			f.IOPrintf("@RSYNCD: " + options.protocolVersion + "\n");		
			string line = f.ReadLine();
			try
			{
				options.remoteProtocol = Int32.Parse(line.Substring(9,2));
			}
			catch 
			{
				options.remoteProtocol = 0;
			}
			bool isValidstring = line.StartsWith("@RSYNCD: ") && line.EndsWith("\n") && options.remoteProtocol > 0;
			if(!isValidstring)
			{
				f.IOPrintf("@ERROR: protocol startup error\n");
				return -1;
			}
			if(options.protocolVersion > options.remoteProtocol)
				options.protocolVersion = options.remoteProtocol;
			f.IOPrintf(path + "\n");
			while(true)
			{
				line = f.ReadLine();
				if (line.CompareTo("@RSYNCD: OK\n") == 0)
					break;				
				if (line.Length > 18 && line.Substring(0, 18).CompareTo("@RSYNCD: AUTHREQD ") == 0) 
				{
					string pass = "";
					if(user.IndexOf(':') != -1)
					{
						pass = user.Substring(user.IndexOf(':')+1);
						user = user.Substring(0, user.IndexOf(':'));				
					}
					f.IOPrintf(user + " " + Authentication.auth_client(user, pass, line.Substring(18).Replace("\n",""), options) + "\n");
					continue;
				}
				
				if (line.CompareTo("@RSYNCD: EXIT\n") == 0)
					MainClass.Exit("@RSYNCD: EXIT", null);
				
				if(line.StartsWith("@ERROR: "))
				{										
					MainClass.Exit("Server: " + line.Replace("\n",""), null);
				}
			}

			for(int i=0; i< sargc; i++)
				f.IOPrintf(sargs[i] + "\n");
			f.IOPrintf("\n");
			return 0;
		}

		public static IOStream OpenSocketOutWrapped(string host, int port, string bindAddress)
		{
			return OpenSocketOut(host,port,bindAddress);
		}

		public static IOStream OpenSocketOut(string host, int port, string bindAddress)
		{
			TcpClient client = null;
			try
			{
				client = new TcpClient(host, port);
			}
			catch(Exception)
			{				
				MainClass.Exit("Can't connect to server", null);
			}
			IOStream stream = new IOStream(client.GetStream());			
			return stream;			
		}

		public static void SetupProtocol(ClientInfo cInfo)
		{
			IOStream f = cInfo.IoStream;
			Options options = cInfo.Options;

			if(options.remoteProtocol == 0)
			{
				if(!options.readBatch)
					f.writeInt(options.protocolVersion);
				options.remoteProtocol = f.readInt();
				if(options.protocolVersion > options.remoteProtocol)
					options.protocolVersion = options.remoteProtocol;
			}
			if(options.readBatch && options.remoteProtocol > options.protocolVersion)
			{				
				MainClass.Exit("The protocol version in the batch file is too new", null);
			}
			if(options.verbose > 3)
			{
				Log.WriteLine("(" + (options.amServer ? "Server" : "Client") + ") Protocol versions: remote="+options.remoteProtocol+", negotiated=" + options.protocolVersion);
			}
			if(options.remoteProtocol < Options.MIN_PROTOCOL_VERSION || options.remoteProtocol > Options.MAX_PROTOCOL_VERSION)
			{				
				MainClass.Exit("Protocol version mistmatch", null);
			}
			if (options.amServer) 
			{
				if (options.checksumSeed == 0)
					options.checksumSeed = (int)System.DateTime.Now.Ticks;
				f.writeInt(options.checksumSeed);
			} 
			else 
				options.checksumSeed = f.readInt();
		}

		public static void PrintRsyncVersion()
		{
			Log.WriteLine(RSYNC_NAME+"  version "+RSYNC_VERSION);
			Log.WriteLine(@"
   This port is Copyright (C) 2006 Alex Pedenko, Michael Feingold and Ivan Semenov
  
   This program is free software; you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation; either version 2 of the License, or
   (at your option) any later version.
 
   This program is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.
 
   You should have received a copy of the GNU General Public License
   along with this program; if not, write to the Free Software
   Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA"); 
		}

		public static void Usage()
		{
			PrintRsyncVersion();

			Log.Write("\nrsync is a file transfer program capable of efficient remote update\nvia a fast differencing algorithm.\n\n");

			Log.Write("Usage: rsync [OPTION]... SRC [SRC]... [USER@]HOST:DEST\n");
			Log.Write("  or   rsync [OPTION]... [USER@]HOST:SRC DEST\n");
			Log.Write("  or   rsync [OPTION]... SRC [SRC]... DEST\n");
			Log.Write("  or   rsync [OPTION]... [USER@]HOST::SRC [DEST]\n");
			Log.Write("  or   rsync [OPTION]... SRC [SRC]... [USER@]HOST::DEST\n");
			Log.Write("  or   rsync [OPTION]... rsync://[USER@]HOST[:PORT]/SRC [DEST]\n");
			Log.Write("  or   rsync [OPTION]... SRC [SRC]... rsync://[USER@]HOST[:PORT]/DEST\n");
			Log.Write("SRC on single-colon remote HOST will be expanded by remote shell\n");
			Log.Write("SRC on server remote HOST may contain shell wildcards or multiple\n");
			Log.Write("  sources separated by space as long as they have same top-level\n");
			Log.Write("\nOptions\n");
			Log.Write(" -v, --verbose               increase verbosity\n");
			Log.Write(" -q, --quiet                 decrease verbosity\n");
			Log.Write(" -c, --checksum              always checksum\n");
			Log.Write(" -a, --archive               archive mode, equivalent to -rlptgoD (no -H)\n");
			Log.Write(" -r, --recursive             recurse into directories\n");
			Log.Write(" -R, --relative              use relative path names\n");
			Log.Write("     --no-relative           turn off --relative\n");
			Log.Write("     --no-implied-dirs       don't send implied dirs with -R\n");
			Log.Write(" -b, --backup                make backups (see --suffix & --backup-dir)\n");
			Log.Write("     --backup-dir            make backups into this directory\n");
			Log.Write("     --suffix=SUFFIX         backup suffix (default "+BACKUP_SUFFIX+" w/o --backup-dir)\n");
			Log.Write(" -u, --update                update only (don't overwrite newer files)\n");
			Log.Write("     --inplace               update destination files inplace (SEE MAN PAGE)\n");
			Log.Write(" -K, --keep-dirlinks         treat symlinked dir on receiver as dir\n");
			Log.Write(" -l, --links                 copy symlinks as symlinks\n");
			Log.Write(" -L, --copy-links            copy the referent of all symlinks\n");
			Log.Write("     --copy-unsafe-links     copy the referent of \"unsafe\" symlinks\n");
			Log.Write("     --safe-links            ignore \"unsafe\" symlinks\n");
			Log.Write(" -H, --hard-links            preserve hard links\n");
			Log.Write(" -p, --perms                 preserve permissions\n");
			Log.Write(" -o, --owner                 preserve owner (root only)\n");
			Log.Write(" -g, --group                 preserve group\n");
			Log.Write(" -D, --devices               preserve devices (root only)\n");
			Log.Write(" -t, --times                 preserve times\n");
			Log.Write(" -S, --sparse                handle sparse files efficiently\n");
			Log.Write(" -n, --dry-run               show what would have been transferred\n");
			Log.Write(" -W, --whole-file            copy whole files, no incremental checks\n");
			Log.Write("     --no-whole-file         turn off --whole-file\n");
			Log.Write(" -x, --one-file-system       don't cross filesystem boundaries\n");
			Log.Write(" -B, --block-size=SIZE       force a fixed checksum block-size\n");
			Log.Write(" -e, --rsh=COMMAND           specify the remote shell\n");
			Log.Write("     --rsync-path=PATH       specify path to rsync on the remote machine\n");
			Log.Write("     --existing              only update files that already exist\n");
			Log.Write("     --ignore-existing       ignore files that already exist on receiving side\n");
			Log.Write("     --delete                delete files that don't exist on the sending side\n");
			Log.Write("     --delete-excluded       also delete excluded files on the receiving side\n");
			Log.Write("     --delete-after          receiver deletes after transferring, not before\n");
			Log.Write("     --ignore-errors         delete even if there are I/O errors\n");
			Log.Write("     --max-delete=NUM        don't delete more than NUM files\n");
			Log.Write("     --partial               keep partially transferred files\n");
			Log.Write("     --partial-dir=DIR       put a partially transferred file into DIR\n");
			Log.Write("     --force                 force deletion of directories even if not empty\n");
			Log.Write("     --numeric-ids           don't map uid/gid values by user/group name\n");
			Log.Write("     --timeout=TIME          set I/O timeout in seconds\n");
			Log.Write(" -I, --ignore-times          turn off mod time & file size quick check\n");
			Log.Write("     --size-only             ignore mod time for quick check (use size)\n");
			Log.Write("     --modify-window=NUM     compare mod times with reduced accuracy\n");
			Log.Write(" -T, --temp-dir=DIR          create temporary files in directory DIR\n");
			Log.Write("     --compare-dest=DIR      also compare destination files relative to DIR\n");
			Log.Write("     --link-dest=DIR         create hardlinks to DIR for unchanged files\n");
			Log.Write(" -P                          equivalent to --partial --progress\n");
			Log.Write(" -z, --compress              compress file data\n");
			Log.Write(" -C, --cvs-exclude           auto ignore files in the same way CVS does\n");
			Log.Write("     --exclude=PATTERN       exclude files matching PATTERN\n");
			Log.Write("     --exclude-from=FILE     exclude patterns listed in FILE\n");
			Log.Write("     --include=PATTERN       don't exclude files matching PATTERN\n");
			Log.Write("     --include-from=FILE     don't exclude patterns listed in FILE\n");
			Log.Write("     --files-from=FILE       read FILE for list of source-file names\n");
			Log.Write(" -0, --from0                 all *-from file lists are delimited by nulls\n");
			Log.Write("     --version               print version number\n");
			Log.Write("     --blocking-io           use blocking I/O for the remote shell\n");
			Log.Write("     --no-blocking-io        turn off --blocking-io\n");
			Log.Write("     --stats                 give some file transfer stats\n");
			Log.Write("     --progress              show progress during transfer\n");
			Log.Write("     --log-format=FORMAT     log file transfers using specified format\n");
			Log.Write("     --password-file=FILE    get password from FILE\n");
			Log.Write("     --bwlimit=KBPS          limit I/O bandwidth, KBytes per second\n");
			Log.Write("     --write-batch=FILE      write a batch to FILE\n");
			Log.Write("     --read-batch=FILE       read a batch from FILE\n");
			Log.Write(" -h, --help                  show this help screen\n");
		}


		public static void Exit(string message, ClientInfo cInfo)
		{
			Log.Write(message);
			
			if(!opt.amDaemon)   
			{
				Console.Read();
				System.Environment.Exit(0);
			}  else
			{
				if(cInfo != null && cInfo.IoStream !=null && cInfo.IoStream.ClientThread != null)
				{
					cInfo.IoStream.ClientThread.Abort();
				}
			}
		}

	}

	class Log
	{
		public static void WriteLine(string str)
		{			
			LogWrite(str);			
		}
		
		public static void Write(string str)
		{
			LogWrite(str);
		}

		public static void LogSend(FileStruct file, Stats initialStats)
		{
		}

		private static void LogWrite(string str)
		{

			if(Daemon.ServerOptions != null)
			{
				if(Daemon.ServerOptions.logFile == null)
				{
					try
					{
						Daemon.ServerOptions.logFile = new FileStream(Path.Combine(Environment.SystemDirectory,"rsyncd.log"), FileMode.OpenOrCreate | FileMode.Append, FileAccess.Write);						
					}										
					catch(Exception e)
					{
						return;
					}
				}
				str = "[ " + DateTime.Now + " ] " + str + "\r\n";				
				Daemon.ServerOptions.logFile.Write(Encoding.ASCII.GetBytes(str),0,str.Length);
				Daemon.ServerOptions.logFile.Flush();				
			} 
			else 
			{
				if(!MainClass.opt.amDaemon )			
					Console.WriteLine(str);			
			}

		}
	}

	public class SumBuf 
	{
		public int offset;
		public UInt32 len;
		public UInt32 sum1;
		public byte flags;
		public byte[] sum2 = new byte[CheckSum.SUM_LENGTH];
	}

	public class SumStruct 
	{
		public int fLength;
		public int count;
		public UInt32 bLength;	
		public UInt32 remainder;
		public int s2Length;
		public SumBuf[] sums;
	}

	public class FStat
	{
		public long size;
		public System.DateTime mTime;
		public int mode;
		public int uid;
		public int gid;
		public int rdev;
	}

	public class Progress
	{
		public static void ShowProgress(long offset, long size)
		{
		}

		public static void EndProgress(long size)
		{
		}
	}
}
