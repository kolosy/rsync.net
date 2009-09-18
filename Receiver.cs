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
using System.IO;
using NetSync;

namespace NetSync
{
	public class Receiver
	{
		private Options options;
		private CheckSum checkSum;

		public Receiver(Options opt)
		{
			options = opt;
			checkSum = new CheckSum(options);
		}
		
		private string LocalizePath(ClientInfo cInfo, string path)
		{
			string normalized = cInfo.Options.dir.Replace('\\', '/').Replace(":", "").ToLower();
			string ret="";
			if (path.ToLower().IndexOf(normalized)!=-1)
				ret = path.Substring(path.ToLower().IndexOf(normalized) + normalized.Length).Replace('/', Path.DirectorySeparatorChar);

			if (ret == "")
				return path.TrimEnd('\\');
			if (ret[0] == Path.DirectorySeparatorChar)
				ret = ret.Substring(1);

			return ret;
		}

		public int ReceiveFiles(ClientInfo cInfo, ArrayList fileList, string localName)
		{
			FStat st = new FStat();
			FileStruct file;
			IOStream f = cInfo.IoStream;
			
			string fileName;
			string fNameCmp = "", fNameTmp = "";		
			bool saveMakeBackups = options.makeBackups;
			int i, phase = 0;
			bool recv_ok;

			if (options.verbose > 2)
				Log.WriteLine("ReceiveFiles(" + fileList.Count + ") starting");		
			while (true) 
			{
				i = f.readInt();
				if (i == -1) {
					if (phase != 0)
						break;

					phase = 1;
					checkSum.cSumLength = CheckSum.SUM_LENGTH;
					if (options.verbose > 2)
						Log.WriteLine("ReceiveFiles phase=" + phase);
					f.writeInt(0); //send_msg DONE
					if (options.keepPartial)
						options.makeBackups = false;
					continue;
				}

				if (i < 0 || i >= fileList.Count) {				
					MainClass.Exit("Invalid file index " + i +" in receiveFiles (count=" + fileList.Count +")", cInfo);
				}

				file = ((FileStruct)fileList[i]);

				Options.stats.currentFileIndex = i;
				Options.stats.numTransferredFiles++;
				Options.stats.totalTransferredSize += file.length;			

				if (localName != null && localName.CompareTo("") != 0)
					fileName = localName;
				else
				{
					fileName = Path.Combine(options.dir,LocalizePath(cInfo, file.FNameTo().Replace(":","")).Replace("\\", "/"));
					//fileName = Path.Combine(options.dir, file.FNameTo().Replace(":","")).Replace("\\", "/");
					// TODO: path length
					FileSystem.Directory.CreateDirectory(Path.Combine(options.dir,LocalizePath(cInfo, file.dirName.Replace(":",""))).Replace("\\", "/"));
					Log.WriteLine(Path.Combine(options.dir, file.dirName));
					//FileSystem.Directory.CreateDirectory(Path.Combine(options.dir,file.dirName.Replace(":","")).Replace("\\", "/"));
				}

				if (options.dryRun) {
					if (!options.amServer && options.verbose > 0)
						Log.WriteLine(fileName);
					continue;
				}

				if (options.verbose > 2)
					Log.WriteLine("receiveFiles(" + fileName + ")");

				if (options.partialDir != null && options.partialDir.CompareTo("") != 0) {			
				} else
					fNameCmp = fileName;
				
				FileStream fd1 = null;
				try
				{
					fd1 = new FileStream(fNameCmp, FileMode.Open, FileAccess.Read);
				}
				catch(FileNotFoundException)
				{
					fNameCmp = fileName;
					try
					{
						fd1 = new FileStream(fNameCmp, FileMode.Open, FileAccess.Read);
					}
					catch(FileNotFoundException)
					{
					}
				}  catch(Exception e)
				{
					Log.Write(e.Message);
				}			
				try
				{
					FileSystem.FileInfo fi = new FileSystem.FileInfo(fNameCmp);
					// TODO: path length
					st.size = fi.Length;
				} 
				catch {}

				String tempFileName = getTmpName(fileName);
				FileStream fd2 = null;			
				fd2 = new FileStream(tempFileName,FileMode.OpenOrCreate,FileAccess.Write);				

				if (!options.amServer && options.verbose > 0)
					Log.WriteLine(fileName);

				/* recv file data */
				recv_ok = ReceiveData(cInfo, fNameCmp, fd1, st.size,
							fileName, fd2, file.length);

				if(fd1 != null)
					fd1.Close();
				if(fd2 != null)
					fd2.Close();
				// TODO: path length
				FileSystem.File.Copy(tempFileName, fileName, true);
				// TODO: path length
				FileSystem.File.Delete(tempFileName);
				if (recv_ok  || options.inplace)
					FinishTransfer(fileName, fNameTmp, file, recv_ok);
			}
			options.makeBackups = saveMakeBackups;

			if (options.deleteAfter && options.recurse && localName == null && fileList.Count > 0)
				DeleteFiles(fileList);

			if (options.verbose > 2)
				Log.WriteLine("ReceiveFiles finished");

			return 0;
		} 
		
		public bool ReceiveData(ClientInfo cInfo, string fileNameR, Stream fdR, long sizeR, string fileName, Stream fd, int totalSize)
		{
			IOStream f = cInfo.IoStream;
			byte[] file_sum1 = new byte[CheckSum.MD4_SUM_LENGTH];
			byte[] file_sum2 = new byte[CheckSum.MD4_SUM_LENGTH];
			byte[] data = new byte[Match.CHUNK_SIZE];
			SumStruct sum = new SumStruct();
			MapFile mapBuf = null;
			Sender sender = new Sender(options);
			sender.ReadSumHead(cInfo, ref sum);
			int offset = 0;
			UInt32 len;
			
			if (fdR != null && sizeR > 0) 
			{
				int mapSize =(int) Math.Max(sum.bLength * 2, 16*1024);
				mapBuf = new MapFile(fdR, (int)sizeR, mapSize, (int)sum.bLength);
				if (options.verbose > 2) 
					Log.WriteLine("recv mapped " + fileNameR + " of size " + sizeR);;
			} 
			Sum s = new Sum(options);
			s.Init(options.checksumSeed);
			
			int i;			
			Token token = new Token(options);
			while ((i = token.ReceiveToken(f, ref data, 0)) != 0) 
			{
				if (options.doProgress)
					Progress.ShowProgress(offset, totalSize);

				if (i > 0) 
				{					
					if (options.verbose > 3) 
						Log.WriteLine("data recv " + i + " at " + offset);
					Options.stats.literalData += i;
					s.Update(data,0,i);					
					if (fd != null && FileIO.WriteFile(fd,data,0,i) != i)
						goto report_write_error;
					offset += i;
					continue;
				}

				i = -(i+1);
				int offset2 = (int)(i*sum.bLength);
				len = sum.bLength;
				if (i == sum.count-1 && sum.remainder != 0)
					len = sum.remainder;

				Options.stats.matchedData += len;

				if (options.verbose > 3)
					Log.WriteLine("chunk["+i+"] of size "+len+" at "+offset2+" offset=" + offset);

				byte[] map = null;
				int off = 0;
				if (mapBuf != null) 
				{
					off = mapBuf.MapPtr(offset2,(int)len);
					map = mapBuf.p;

					token.SeeToken(map, offset, (int)len);
					s.Update(map, off, (int)len);
				}

				if (options.inplace) 
				{
					if (offset == offset2 && fd != null) 
					{
						offset += (int)len;
						if (fd.Seek(len, SeekOrigin.Current) != offset) 
						{							
							MainClass.Exit("seek failed on "+ Util.fullFileName(fileName),cInfo);
						}
						continue;
					}
				}				
				if (fd != null && FileIO.WriteFile(fd, map, off, (int)len) != (int)len)
					goto report_write_error;
				offset += (int)len;
			}

			if (options.doProgress)
				Progress.EndProgress(totalSize);
			if (fd != null && offset > 0 && FileIO.SparseEnd(fd) != 0) 
			{				
				MainClass.Exit("write failed on " + Util.fullFileName(fileName),cInfo);
			}

			file_sum1 = s.End();

			if (mapBuf != null)
				mapBuf = null;
			
			file_sum2 = f.ReadBuf(CheckSum.MD4_SUM_LENGTH);
			if (options.verbose > 2)
				Log.WriteLine("got fileSum");
			if (fd != null && Util.MemCmp(file_sum1,0, file_sum2,0, CheckSum.MD4_SUM_LENGTH) != 0)
				return false;
			return true;
			report_write_error:
			{
				MainClass.Exit("write failed on " + Util.fullFileName(fileName),cInfo);
			}
			return true;
		}

		public static void FinishTransfer(string fileName,string fileNameTmp, FileStruct file, bool okToSetTime)
		{
		}
 
		public void deleteOne(string fileName, bool isDir)
		{
			SysCall sc = new SysCall(options);
			if(!isDir)
			{

				if(!sc.robustUnlink(fileName))
					Log.WriteLine("Can't delete '"+fileName+"' file");
				else
					if(options.verbose > 0)
						Log.WriteLine("deleting file "+fileName);
			} 
			else
			{
				if(!sc.doRmDir(fileName))
					Log.WriteLine("Can't delete '"+fileName+"' dir");
				else
					if(options.verbose > 0)
						Log.WriteLine("deleting directory "+fileName);
			}
		}

		public bool isBackupFile(string fileName)
		{
			return fileName.EndsWith(options.backupSuffix);
		}

		public string getTmpName(string fileName)
		{
			return Path.GetTempFileName();	
		}

		public void DeleteFiles(ArrayList fileList)
		{
			string[] argv = new string[1];
			ArrayList localFileList = null;
			if(options.cvsExclude)
				Exclude.AddCvsExcludes();
			for(int j=0; j<fileList.Count; j++)
			{
				if((((FileStruct)fileList[j]).mode & Options.FLAG_TOP_DIR) == 0  || !Util.S_ISDIR(((FileStruct)fileList[j]).mode))
					continue;
				argv[0] = options.dir +((FileStruct)fileList[j]).FNameTo();	
				FileList fList = new FileList(options);
				if((localFileList = fList.sendFileList(null, argv)) == null)
				   continue;
				for (int i = localFileList.Count-1; i >= 0; i--) 
				{					
					if(((FileStruct)localFileList[i]).baseName == null)
						continue;
					((FileStruct)localFileList[i]).dirName = ((FileStruct)localFileList[i]).dirName.Substring(options.dir.Length);
					if (FileList.fileListFind(fileList,((FileStruct)localFileList[i])) < 0) 
					{
						((FileStruct)localFileList[i]).dirName = options.dir + ((FileStruct)localFileList[i]).dirName;
						deleteOne(((FileStruct)localFileList[i]).FNameTo(),Util.S_ISDIR(((FileStruct)localFileList[i]).mode));
					}
				}
			}
		}
	}

	

	public class SysCall
	{

		private Options options;

		public SysCall(Options opt)
		{
			options = opt;
		}

		public bool doRmDir(string pathName)
		{
			if(options.dryRun)
				return true;
			if(options.readOnly || options.listOnly)
				return false;
			try
			{
				// TODO: path length
				FileSystem.Directory.Delete(pathName);
				return true;
			}
			catch
			{
				return false;
			}
		}

		public bool robustUnlink(string fileName)
		{
			return doUnlink(fileName);
		}

		public bool doUnlink(string fileName)
		{
			if(options.dryRun)
				return true;
			if(options.readOnly || options.listOnly)
				return false;
			try
			{
				// TODO: path length
				FileSystem.File.Delete(fileName);
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}
