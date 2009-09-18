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
using System.Drawing;
using System.IO;
using System.Collections;
using System.Windows.Forms;
using NetSync;

namespace NetSync
{
	class Sender
	{
		private Options options;
		private CheckSum checkSum;

		public Sender(Options opt)
		{
			options = opt;
			checkSum = new CheckSum(options);
		}

		public void SendFiles(ArrayList fileList, ClientInfo cInfo)
		{
			ShowMessage("Processing...");
			try 
			{
				IOStream f = cInfo.IoStream;
				string fileName = "", fileName2 = "";
				SumStruct s = null;
				int phase = 0;
				bool saveMakeBackups = options.makeBackups;			
				Match match = new Match(options);

				if( options.verbose > 2)
					Log.WriteLine("SendFiles starting");			
				while (true)
				{
					fileName = "";				
					int i = f.readInt();
					if (i == -1) 
					{
						if (phase == 0) 
						{
							phase++;
							checkSum.cSumLength = CheckSum.SUM_LENGTH;
							f.writeInt(-1);
							if (options.verbose > 2)
								Log.WriteLine("SendFiles phase=" + phase);
							options.makeBackups = false;																	
							continue;
						}
						break;
					}

					if (i < 0 || i >= fileList.Count) 
					{										
						MainClass.Exit("Invalid file index " + i + " (count=" + fileList.Count + ")",cInfo);
					}

					FileStruct file = (FileStruct)fileList[i];

					Options.stats.currentFileIndex = i;
					Options.stats.numTransferredFiles++;
					Options.stats.totalTransferredSize += file.length;

					if (file.baseDir != null && file.baseDir.CompareTo("") != 0) 
					{
						fileName = file.baseDir;
						if(!fileName.EndsWith("/"))
							fileName += "/";
					} 
					fileName2 = file.FNameTo();
					fileName +=  file.FNameTo();
					ShowMessage("uploading " + fileName);

					if (options.verbose > 2)
						Log.WriteLine("sendFiles(" + i + ", " + fileName + ")");

					if (options.dryRun) 
					{
						if (!options.amServer && options.verbose != 0)
							Log.WriteLine(fileName2);
						f.writeInt(i);
						continue;
					}

					Stats initialStats = Options.stats;
					s = ReceiveSums(cInfo);

					Stream fd;
					try
					{
						fd = new FileStream(fileName, FileMode.Open, FileAccess.Read);
					}
					catch(FileNotFoundException)
					{				
						Log.WriteLine("file has vanished: " + Util.fullFileName(fileName));
						s = null;
						continue;
					}
					catch(Exception)
					{					
						Log.WriteLine("SendFiles failed to open " + Util.fullFileName(fileName));
						s = null;
						continue;
					}

					FStat st = new FStat();
					FileSystem.FileInfo fi = new FileSystem.FileInfo(fileName);
					// TODO: path length
					st.mTime = fi.LastWriteTime;
					// TODO: path length
					st.size = fi.Length;

					MapFile mbuf = null;
					if (st.size != 0) 
					{
						int mapSize = (int)Math.Max(s.bLength * 3, Options.MAX_MAP_SIZE);
						mbuf = new MapFile(fd, (int)st.size, mapSize, (int)s.bLength);
					} 

					if (options.verbose > 2) 
						Log.WriteLine("SendFiles mapped " + fileName + " of size " + st.size);

					f.writeInt(i);
					Generator gen = new Generator(options);
					gen.WriteSumHead(f, s);

					if (options.verbose > 2) 
						Log.WriteLine("calling MatchSums " + fileName);

					if (!options.amServer && options.verbose != 0)
						Log.WriteLine(fileName2);

					Token token = new Token(options);
					token.SetCompression(fileName);

					match.MatchSums(f, s, mbuf, (int)st.size);
					Log.LogSend(file, initialStats);

					if (mbuf != null) 
					{
						bool j = mbuf.UnMapFile();
						if (j) 
						{						
							Log.WriteLine("read errors mapping " + Util.fullFileName(fileName));
						}
					}
					fd.Close();

					s.sums = null;

					if (options.verbose > 2) 
						Log.WriteLine("sender finished " + fileName);
				}
				options.makeBackups = saveMakeBackups;

				if (options.verbose > 2)
					Log.WriteLine("send files finished");

				match.MatchReport(f);
				f.writeInt(-1);			
			}
			finally
			{
				HideMessage();
			}
		}

		public SumStruct ReceiveSums(ClientInfo cInfo)
		{
			IOStream f = cInfo.IoStream;
			SumStruct s = new SumStruct();
			int i;
			int offset = 0;
			ReadSumHead(cInfo, ref s);
			s.sums = null;

			if (options.verbose > 3) 
				Log.WriteLine("count=" + s.count + " n=" + s.bLength + " rem=" + s.remainder);

			if (s.count == 0)
				return s;

			s.sums = new SumBuf[s.count];

			for (i = 0; i < s.count; i++) 
			{
				s.sums[i] = new SumBuf();
				s.sums[i].sum1 = (UInt32)f.readInt();				
				s.sums[i].sum2 = f.ReadBuf(s.s2Length);
				s.sums[i].offset = offset;
				s.sums[i].flags = 0;

				if (i == s.count-1 && s.remainder != 0)
					s.sums[i].len = s.remainder;
				else
					s.sums[i].len = s.bLength;
				offset += (int)s.sums[i].len;

				if (options.verbose > 3) 
					Log.WriteLine("chunk[" + i + "] len=" + s.sums[i].len);
			}

			s.fLength = offset;
			return s;
		}

		public void ReadSumHead(ClientInfo cInfo, ref SumStruct sum)
		{
			IOStream f = cInfo.IoStream;
			sum.count = f.readInt();
			sum.bLength = (UInt32)f.readInt();
			if (options.protocolVersion < 27) 
				sum.s2Length = checkSum.cSumLength;
			else 
			{
				sum.s2Length = f.readInt();
				if (sum.s2Length > CheckSum.MD4_SUM_LENGTH) 
				{					
					MainClass.Exit("Invalid checksum length " + sum.s2Length, cInfo);
				}
			}
			sum.remainder = (UInt32)f.readInt();
		}

		NotifyIcon icon = new NotifyIcon();
		public void ShowMessage(string msg)
		{
			// TODO: path length
			if (!FileSystem.File.Exists("logo.ico"))
				return;

			if (msg.Length > 64)
				msg = msg.Substring(0, 60) + "...";

			icon.Icon = new Icon("logo.ico");
			icon.Text = msg;
			icon.Visible = true;
		}

		public void HideMessage()
		{
			icon.Visible = false;
		}
	}

}
