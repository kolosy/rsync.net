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
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NetSync
{

	public class ClientInfo
	{
		public Options Options = null;
		public IOStream IoStream = null;		
		public Thread ClientThread = null;		
	}

	public class TCPSocketListener
	{
		private Socket Client;
		private Thread ClientThread;
		private ClientInfo ClientInfo;
		private ArrayList ClientSockets;

		public TCPSocketListener(Socket client, ref ArrayList clientSockets)
		{
			Client = client;
			ClientSockets = clientSockets;
			ClientInfo = new ClientInfo();
		    ClientInfo.Options = new Options();
		}
		public void StartSocketListener()
		{	
			if (Client!= null)
			{
				ClientThread = new Thread(new ThreadStart(StartDaemon));
				ClientInfo.IoStream = new IOStream(new NetworkStream (Client));			
				ClientInfo.IoStream.ClientThread = ClientThread;
				ClientThread.Start();				
			}			
		}

		public void StartDaemon()
		{
			string remoteAddr = Client.RemoteEndPoint.ToString();
			remoteAddr = remoteAddr.Substring(0,remoteAddr.IndexOf(':'));						
			string remoteHost = Dns.GetHostByAddress(IPAddress.Parse(remoteAddr)).HostName;
			ClientInfo.Options.remoteAddr = remoteAddr;
			ClientInfo.Options.remoteHost = remoteHost;
			
			
			Daemon.StartDaemon(ClientInfo);
			Client.Close();			
			ClientSockets.Remove(this);
		}
	}


	public class Daemon
	{
		private static TcpListener Server = null;
		private static ArrayList ClientSockets = null;
		private static bool StopServer = true;
		public static Options ServerOptions = null;

		public static Configuration config;

		public static int DaemonMain(Options options)
		{
			ServerOptions = options;
			config = new Configuration(ServerOptions.configFile);
			if(config.LoadParm(options))
			{				
				StartAcceptLoop(options.rsyncPort);
			}
			return -1;
		}

		public static void StartAcceptLoop(int port)
		{
			IPAddress localAddr = IPAddress.Parse(ServerOptions.bindAddress);			
//			Server = new TcpListener(localAddr, port);
			Server = new TcpListener(port);
			try
			{
				Server.Start();			
			} catch (Exception)
			{
				MainClass.Exit("Can't listening address " + ServerOptions.bindAddress + " on port " + port, null);
				System.Environment.Exit(0);
			}
			Log.WriteLine("WinRSyncd starting, listening on port " + port);
			StopServer = false;
			ClientSockets = new ArrayList();
			while(!StopServer)
			{
				try
				{
					Socket soc = Server.AcceptSocket();					
					if(!config.LoadParm(ServerOptions))
						continue;
					TCPSocketListener socketListener = new TCPSocketListener(soc, ref ClientSockets);					
					lock(ClientSockets)
					{						
						ClientSockets.Add(socketListener);
					}
					socketListener.StartSocketListener();
					for(int i=0; i < ClientSockets.Count; i++)
					{
						if(ClientSockets[i] == null)
							ClientSockets.RemoveAt(i);
					}
				}
				catch (SocketException)
				{
					StopServer = true;
				}
			}
			if(ServerOptions.logFile != null)
				ServerOptions.logFile.Close();
		}

		public static int StartDaemon(ClientInfo cInfo)
		{			
			IOStream stream = cInfo.IoStream;
			Options options = cInfo.Options;
			options.amDaemon = true;

			stream.IOPrintf("@RSYNCD: " + options.protocolVersion + "\n");
			string line = stream.ReadLine();
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
				stream.IOPrintf("@ERROR: protocol startup error\n");
				return -1;
			}
			if(options.protocolVersion > options.remoteProtocol)
				options.protocolVersion = options.remoteProtocol;
			line = stream.ReadLine();
			if(line.CompareTo("#list\n") == 0)
			{
				ClientServer.SendListing(stream);
				return -1;
			}

			if (line[0] == '#') 
			{
				stream.IOPrintf("@ERROR: Unknown command '" + line.Replace("\n","") + "'\n");
				return -1;
			}

			int i = config.GetNumberModule(line.Replace("\n",""));
			if(i < 0)
			{
				stream.IOPrintf("@ERROR: Unknown module " + line);
				MainClass.Exit("@ERROR: Unknown module " + line, cInfo);
			}
			options.doStats = true;	
			options.ModuleId = i;
			ClientServer.RsyncModule(cInfo, i);
			cInfo.IoStream.Close();
			return 1;
		}

		public static void StartServer(ClientInfo cInfo, string[] args)
		{		
			IOStream f = cInfo.IoStream;
			Options options = cInfo.Options;
	
			if(options.protocolVersion >= 23)
				f.IOStartMultiplexOut();
			if (options.amSender) 
			{
				options.keepDirLinks = false; /* Must be disabled on the sender. */				
				Exclude excl = new Exclude(options);
				excl.ReceiveExcludeList(f);
				DoServerSender(cInfo, args);
			} 
			else 
			{
				DoServerReceive(cInfo, args);
			}
		}

		static void DoServerSender(ClientInfo cInfo, string[] args)
		{			
			string dir = args[0];
			IOStream f = cInfo.IoStream;
			Options options = cInfo.Options;

			if(options.verbose > 2) 
			{
				Log.Write("Server sender starting");
			}			
			if (options.amDaemon && config.ModuleIsWriteOnly(options.ModuleId)) 
			{				
				MainClass.Exit("ERROR: module " + config.GetModuleName(options.ModuleId) + " is write only", cInfo);
				return;
			}

			if (options.relativePaths == 0 && !Util.pushDir(dir)) 
			{				
				MainClass.Exit("push_dir#3 " + dir + "failed", cInfo);
				return;
			}

			FileList fList = new FileList(options);
			ArrayList fileList = fList.sendFileList(cInfo, args);
			if(options.verbose > 3)
				Log.WriteLine("file list sent");
			if (fileList.Count == 0) 
			{
				MainClass.Exit("File list is empty", cInfo);
				return;
			}			
			f.IOStartBufferingIn();
			f.IOStartBufferingOut();
			
			Sender sender = new Sender(options);
			sender.SendFiles(fileList, cInfo);
			f.Flush();
			MainClass.Report(cInfo);
			if(options.protocolVersion >= 24)
				f.readInt();			
			f.Flush();		
		}

		public static void DoServerReceive(ClientInfo cInfo, string[] args)
		{	
			Options options = cInfo.Options;
			IOStream f = cInfo.IoStream;
			if (options.verbose > 2) 
			{
				Log.Write("Server receive starting");
			}
			if (options.amDaemon && config.ModuleIsReadOnly(options.ModuleId)) 
			{				
				MainClass.Exit("ERROR: module " + config.GetModuleName(options.ModuleId) + " is read only", cInfo);
				return;
			}

			f.IOStartBufferingIn();
			if (options.deleteMode && !options.deleteExcluded)
			{
				Exclude excl = new Exclude(options);
				excl.ReceiveExcludeList(f);
			}

			FileList fList = new FileList(cInfo.Options);
			ArrayList fileList = fList.receiveFileList(cInfo);				
			DoReceive(cInfo, fileList,null); 
		}

		public static int DoReceive(ClientInfo cInfo, ArrayList fileList ,string localName)
		{					
			IOStream f = cInfo.IoStream;
			Options options = cInfo.Options;
			Receiver receiver = new Receiver(options);

			options.copyLinks = false;			
			f.Flush();			
			if (!options.deleteAfter) 
			{
				if (options.recurse && options.deleteMode && localName == null && fileList.Count > 0)
					receiver.DeleteFiles(fileList);
			}
			f.IOStartBufferingOut();
			Generator gen = new Generator(options);
			gen.GenerateFiles(f, fileList, localName);
			f.Flush();			
			if(fileList != null && fileList.Count != 0)				
				receiver.ReceiveFiles(cInfo ,fileList,localName);
			MainClass.Report(cInfo);
			if (options.protocolVersion >= 24) 
			{
				/* send a final goodbye message */
				f.writeInt(-1);
			} 
			f.Flush();				
			return 0;
		} 
	}
}
